
using HideVolumeOSD.Properties;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;

namespace HideVolumeOSD
{
	public class HideVolumeOSDLib
	{
		[DllImport("user32.dll")]
		private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		[DllImport("user32.dll")]
		private static extern bool IsWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool SystemParametersInfo(uint action, IntPtr param, [Out] out RECT rect, IntPtr init);

		[DllImport("user32.dll")]
		static extern int GetSystemMetrics(int sm);


		const int SM_CXSCREEN = 0;
		const int SM_CYSCREEN = 1;


		const int WM_APPCOMMAND = 0x319;

		const int APPCOMMAND_VOLUME_MUTE = 0x80000;
		const int APPCOMMAND_VOLUME_DOWN = 0x90000;
		const int APPCOMMAND_VOLUME_UP = 0xA0000;


		const int SPI_GETWORKAREA = 0x0030;


		private struct NOTIFYICONIDENTIFIER
		{
			public uint cbSize;
			public IntPtr hWnd;
			public uint uID;
			public Guid guidItem;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct RECT
		{
			public int left;
			public int top;
			public int right;
			public int bottom;
		}

		[DllImport("Shell32.dll", SetLastError = true)]
		private static extern Int32 Shell_NotifyIconGetRect([In] ref NOTIFYICONIDENTIFIER identifier, [Out] out RECT iconLocation);

		NotifyIcon notifyIcon;
		NOTIFYICONIDENTIFIER notifyIconIdentifier;

		IntPtr hWndInject = IntPtr.Zero;
	
		VolumePoup volumePopup;

		System.Windows.Forms.Timer hideTimer = new System.Windows.Forms.Timer();		

		public HideVolumeOSDLib(NotifyIcon ni)
		{
			if (ni != null)
			{
				this.notifyIcon = ni;
			}
		}

		public void Init()
		{
			hWndInject = FindOSDWindow(true);

			int count = 1;

			while (hWndInject == IntPtr.Zero && count < 9)
			{
				internalShowOSD(true);

				hWndInject = FindOSDWindow(true);

				// Quadratic backoff if the window is not found
				System.Threading.Thread.Sleep(1000*(count^2));
				count++;		
			}

			// final try

			hWndInject = FindOSDWindow(false);

			if (hWndInject == IntPtr.Zero)
			{
				Program.InitFailed = true;
				return;
			}

			if (notifyIcon != null)
			{
				volumePopup = new VolumePoup();

				Application.ApplicationExit += Application_ApplicationExit;

				if (Settings.Default.HideOSD)                                                                                                                                                                                                                       
					HideOSD();
				else
					ShowOSD();				

				FieldInfo idFieldInfo = notifyIcon.GetType().GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
				int iconID = (int)idFieldInfo.GetValue(notifyIcon);


				FieldInfo windowFieldInfo = notifyIcon.GetType().GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
				System.Windows.Forms.NativeWindow nativeWindow = (System.Windows.Forms.NativeWindow)windowFieldInfo.GetValue(notifyIcon);
				IntPtr iconhandle = nativeWindow.Handle;

				notifyIconIdentifier = new NOTIFYICONIDENTIFIER()
				{
					hWnd = iconhandle,
					uID = (uint)iconID
				};

				notifyIconIdentifier.cbSize = (uint)Marshal.SizeOf(notifyIconIdentifier);

				KeyHook.VolumeKeyPressed += KeyHook_VolumeKeyPressed;
				KeyHook.VolumeKeyReleased += KeyHook_VolumeKeyReleased;
			
				KeyHook.StartListening();
			
				hideTimer.Tick += HideTimer_Tick;			
			}
		}

        private IntPtr FindOSDWindow(bool bSilent)
		{
			IntPtr hwndOSD = IntPtr.Zero;

			String build = RuntimeInformation.OSDescription.Substring(RuntimeInformation.OSDescription.LastIndexOf('.') + 1);
			int buildNumber = int.Parse(build);

			if (buildNumber >= 22000)
            {
				hwndOSD = internalFind(bSilent, "XamlExplorerHostIslandWindow", "", "Windows.UI.Composition.DesktopWindowContentBridge", "DesktopWindowXamlSource"); 
			}
            else 
			{
				hwndOSD = internalFind(bSilent, "NativeHWNDHost", "", "DirectUIHWND", "");
			}
			
			// if no window found yet, there is no OSD window at all

			if (hwndOSD == IntPtr.Zero && !bSilent)
			{
				ShowMessage("Sorry, the OSD window could not be found! Application is closed...", ToolTipIcon.Error);
			}

			return hwndOSD;
		}

		private IntPtr internalFind(bool bSilent, String outerClass, String outerName, String innerClass, String innerName)
        {
			IntPtr hwndFound = IntPtr.Zero;
			IntPtr hwndOSD = IntPtr.Zero;

			int pairCount = 0;

			// search for all windows with with outClass and outerName

			while ((hwndFound = FindWindowEx(IntPtr.Zero, hwndFound, outerClass, outerName)) != IntPtr.Zero)
			{
				// search for all child windows with with innerClass and innerName

				if (FindWindowEx(hwndFound, IntPtr.Zero, innerClass, innerName) != IntPtr.Zero)
				{
					// if this is the only pair we are sure

					if (pairCount == 0)
					{
						hwndOSD = hwndFound;
					}

					pairCount++;

					// if there are more pairs the criteria has failed...

					if (pairCount > 1)
					{
						//ShowMessage("OSD window not clearly found,\nmultiple pairs exist!\nApplication is closed...", ToolTipIcon.Error);
						//return IntPtr.Zero;
					}
				}
			}

			return hwndOSD;
		}

		private void Application_ApplicationExit(object sender, EventArgs e)
		{
			volumePopup.Stop();
			KeyHook.StopListening();
			ShowOSD();
		}

		private void KeyHook_VolumeKeyPressed(object sender, EventArgs e)
		{			
			if (Settings.Default.VolumeInSystemTray && Settings.Default.HideOSD)
			{
				hideTimer.Stop();
				showVolumeWindow(true);
			}
		}
		
		private void KeyHook_VolumeKeyReleased(object sender, EventArgs e)
		{
			if (Settings.Default.VolumeInSystemTray && Settings.Default.HideOSD)
			{
				hideTimer.Interval = Settings.Default.VolumeHideDelay;
				hideTimer.Start();
			}
		}

		private void HideTimer_Tick(object sender, EventArgs e)
		{
			hideTimer.Stop();

			if (Settings.Default.VolumeInSystemTray)
			{
				showVolumeWindow(false);
			}
		}

		public void HideOSD()
		{
            if (!IsWindow(hWndInject))
            {
                Init();
            }

			ShowWindow(hWndInject, 6); // SW_MINIMIZE

			if (notifyIcon != null)
				notifyIcon.Icon = Resources.IconDisabled;
		}

		private void internalShowOSD(bool init = false)
        {
			float volume = volumePopup.getVolume();

			hideTimer.Stop();
			showVolumeWindow(false);

			if (volume == 1)
			{
				if (init)
				{
					keybd_event((byte)Keys.VolumeUp, 0, 0, 0);
				}
				else
				{
					SendMessage(IntPtr.Zero, WM_APPCOMMAND, IntPtr.Zero, (IntPtr)APPCOMMAND_VOLUME_UP);
				}
			}
			else
			{
				if (init)
				{
					keybd_event((byte)Keys.VolumeUp, 0, 0, 0);
					keybd_event((byte)Keys.VolumeDown, 0, 0, 0);
				}
				else
				{
					SendMessage(IntPtr.Zero, WM_APPCOMMAND, IntPtr.Zero, (IntPtr)APPCOMMAND_VOLUME_UP);
					SendMessage(IntPtr.Zero, WM_APPCOMMAND, IntPtr.Zero, (IntPtr)APPCOMMAND_VOLUME_DOWN);
				}
			}
		}

		public void ShowOSD()
		{
            if (!IsWindow(hWndInject))
            {
                Init();
            }

			ShowWindow(hWndInject, 9); // SW_RESTORE


			if (notifyIcon != null)
			{
				// show window on the screen

				internalShowOSD();

				notifyIcon.Icon = Resources.Icon;
			}
		}

		public void ShowMessage(String message, ToolTipIcon icon)
        {
			notifyIcon.ShowBalloonTip(5000, "HideVolumeOSD", message, icon);

			long tickCountEnd = Environment.TickCount + 5000;
			
			while (Environment.TickCount < tickCountEnd)
			{
				Application.DoEvents();
			}
		}

		public void showVolumeWindow(bool bShow)
        {
			if (bShow)
            {
				RECT rect = new RECT();

				bool bOverIcon = false;

				if (Shell_NotifyIconGetRect(ref notifyIconIdentifier, out rect) != 0 || Settings.Default.VolumeDisplayNearClock)
				{
					RECT rcDesktop = new RECT();
					SystemParametersInfo(SPI_GETWORKAREA, IntPtr.Zero, out rcDesktop, IntPtr.Zero);

					int cx = GetSystemMetrics(SM_CXSCREEN);
					int cy = GetSystemMetrics(SM_CYSCREEN);

					int taskBarHeight = cy - rcDesktop.bottom;

					rect.left = (int)(cx - taskBarHeight * 1.8);
					rect.right = cx;

					rect.top = rcDesktop.bottom;
					rect.bottom = cy;
				}
				else
                {
					bOverIcon = true;
                }

				int height = rect.bottom - rect.top;

				switch (Settings.Default.VolumeDisplaySize)
				{
					case 0:

						height = (int)(height / 2.75);
						break;

					case 1:

						height = (int)(height / 2);
						break;

					case 2:

						height = (int)(height / 1.2);
						break;
				}

				int width = (int)(height * 1.8);

				volumePopup.Show();
				volumePopup.Size = new Size(width, height);

				if (bOverIcon)
					volumePopup.Location = new Point(rect.left + (rect.right - rect.left) / 2 - width / 2, rect.top + (rect.bottom - rect.top) / 2 - height / 2);
				else
					volumePopup.Location = new Point(rect.right - width - Settings.Default.VolumeDisplayOffset, rect.top + (rect.bottom - rect.top) / 2 - height / 2);
			}
			else
            {
				volumePopup.Hide();
            }
		}
	}
}
