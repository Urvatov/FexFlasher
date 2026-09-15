using System.Management;

namespace FexFlasher
{
	public class MainForm : Form
	{
		private readonly ComboBox driveSelector;
		private readonly Button refreshButton;
		private readonly Button flashButton;
		private readonly ProgressBar progressBar;
		private readonly TextBox logBox;
		private readonly OpenFileDialog openFileDialog;

		public MainForm()
		{
			Text = "Fex Flasher";
			Width = 650;
			Height = 480;
			StartPosition = FormStartPosition.CenterScreen;
			MinimumSize = new Size(500, 400);

			driveSelector = new ComboBox
			{
				Dock = DockStyle.Top,
				DropDownStyle = ComboBoxStyle.DropDownList,
				Height = 30
			};

			refreshButton = new Button
			{
				Text = "Refresh Drives",
				Dock = DockStyle.Top,
				Height = 32
			};
			refreshButton.Click += (s, e) => PopulateDrives();

			flashButton = new Button
			{
				Text = "Select .fex and Flash",
				Dock = DockStyle.Top,
				Height = 40
			};
			flashButton.Click += FlashButton_Click;

			progressBar = new ProgressBar
			{
				Dock = DockStyle.Top,
				Height = 22,
				Minimum = 0,
				Maximum = 100,
				Value = 0
			};

			logBox = new TextBox
			{
				Multiline = true,
				Dock = DockStyle.Fill,
				ScrollBars = ScrollBars.Vertical,
				ReadOnly = true,
				Font = new Font("Consolas", 9.5f)
			};

			// Add controls in reverse dock order:
			// Top: driveSelector, then refreshButton, then flashButton, then progressBar, Fill: logBox
			Controls.Add(logBox);
			Controls.Add(progressBar);
			Controls.Add(flashButton);
			Controls.Add(refreshButton);
			Controls.Add(driveSelector);

			string defaultFilesDir = Path.Combine(AppContext.BaseDirectory, "files");
			openFileDialog = new OpenFileDialog
			{
				Filter = "FEX files (*.fex)|*.fex|All files (*.*)|*.*",
				InitialDirectory = Directory.Exists(defaultFilesDir) ? defaultFilesDir : AppContext.BaseDirectory
			};

			PopulateDrives();
		}

		private void PopulateDrives()
		{
			driveSelector.Items.Clear();

			try
			{
				var drives = DriveInfo.GetDrives()
					.Where(d => d.DriveType == DriveType.Removable && d.IsReady)
					.ToList();

				foreach (var drive in drives)
				{
					string display = $"{drive.Name} ({FormatSize(drive.TotalSize)})";
					driveSelector.Items.Add(display);
				}

				if (driveSelector.Items.Count > 0)
				{
					driveSelector.SelectedIndex = 0;
					flashButton.Enabled = true;
				}
				else
				{
					driveSelector.Items.Add("No removable drives found");
					driveSelector.SelectedIndex = 0;
					flashButton.Enabled = false;
				}
			}
			catch (Exception ex)
			{
				Log($"Error querying drives: {ex.Message}");
			}
		}

		private async void FlashButton_Click(object? sender, EventArgs e)
		{
			if (driveSelector.SelectedItem == null)
			{
				Log("No drive selected.");
				return;
			}

			string selected = driveSelector.SelectedItem.ToString()!;
			if (selected == "No removable drives found" || selected.Length < 2)
			{
				MessageBox.Show("Please connect an SD card / removable drive and click 'Refresh Drives'.", "No Drive", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			if (openFileDialog.ShowDialog() != DialogResult.OK)
			{
				return;
			}

			string driveLetter = selected.Substring(0, 2); // e.g. "F:"

			var confirm = MessageBox.Show(
				$"⚠️ WARNING ⚠️\n\nYou are about to overwrite raw data on:\n\n" +
				$"{selected}\n\nThis will destroy existing data on the SD card.\n\nProceed?",
				"Confirm Flash",
				MessageBoxButtons.YesNo,
				MessageBoxIcon.Warning);

			if (confirm != DialogResult.Yes)
			{
				Log("Flash cancelled by user.");
				return;
			}

			string? physicalDrive = GetPhysicalDriveForLetter(driveLetter);
			if (physicalDrive == null)
			{
				Log("Failed to resolve physical drive.");
				MessageBox.Show("Failed to resolve physical drive for " + driveLetter, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			string fexPath = openFileDialog.FileName;
			Log($"Flashing {Path.GetFileName(fexPath)} to {physicalDrive} ...");

			SetControlsEnabled(false);
			progressBar.Value = 0;

			try
			{
				await Task.Run(() =>
				{
					// Equivalent to: dd if=file.fex of=drive bs=1k seek=16400
					using FileStream input = new FileStream(fexPath, FileMode.Open, FileAccess.Read);
					using FileStream output = new FileStream(physicalDrive, FileMode.Open, FileAccess.ReadWrite);

					const int blockSize = 1024;
					long seek = 16400L * blockSize;

					output.Seek(seek, SeekOrigin.Begin);

					byte[] buffer = new byte[blockSize];
					int bytesRead;
					long totalWritten = 0;
					long fileSize = input.Length;
					int lastPercent = -1;

					while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
					{
						output.Write(buffer, 0, bytesRead);
						totalWritten += bytesRead;

						int percent = (int)((totalWritten * 100) / fileSize);
						if (percent != lastPercent && percent % 5 == 0)
						{
							lastPercent = percent;
							Invoke(() =>
							{
								progressBar.Value = Math.Clamp(percent, 0, 100);
								Log($"Progress: {percent}%");
							});
						}
					}

					output.Flush();
					Invoke(() =>
					{
						progressBar.Value = 100;
						Log($"✅ Done! Wrote {totalWritten} bytes.");
					});
				});

				MessageBox.Show("Flashing completed successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				Log("❌ Error: " + ex.Message);
				MessageBox.Show("Error flashing drive:\n\n" + ex.Message, "Flash Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				SetControlsEnabled(true);
			}
		}

		private void SetControlsEnabled(bool enabled)
		{
			flashButton.Enabled = enabled;
			refreshButton.Enabled = enabled;
			driveSelector.Enabled = enabled;
		}

		private string? GetPhysicalDriveForLetter(string letter)
		{
			string driveLetter = letter.TrimEnd('\\');

			using var searcher = new ManagementObjectSearcher(
				$"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

			foreach (ManagementObject partition in searcher.Get())
			{
				using var diskSearcher = new ManagementObjectSearcher(
					$"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");

				foreach (ManagementObject disk in diskSearcher.Get())
				{
					return disk["DeviceID"]?.ToString(); // e.g. "\\\\.\\PHYSICALDRIVE2"
				}
			}
			return null;
		}

		private void Log(string msg)
		{
			if (logBox.InvokeRequired)
			{
				logBox.Invoke(new Action(() => Log(msg)));
				return;
			}
			logBox.AppendText(msg + Environment.NewLine);
		}

		private static string FormatSize(long size)
		{
			string[] sizes = { "B", "KB", "MB", "GB", "TB" };
			double len = size;
			int order = 0;
			while (len >= 1024 && order < sizes.Length - 1)
			{
				order++;
				len /= 1024;
			}
			return $"{len:0.##} {sizes[order]}";
		}
	}
}
