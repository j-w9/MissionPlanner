using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AppKit;
using Foundation;
using MissionPlanner.ArduPilot;
using Xamarin.Forms.Platform.MacOS;
using Xamarin.Forms;
using MissionPlanner.Comms;
using MissionPlanner.Utilities;
using Xamarin.GCSViews;
using Device = Xamarin.Forms.Device;

namespace Xamarin.MacOS
{
    [Register("AppDelegate")]
    public class AppDelegate : FormsApplicationDelegate
    {
        public AppDelegate()
        {
            var style = NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable | NSWindowStyle.Titled;
            var rect = new CoreGraphics.CGRect(200,200,1024,768);
            mainWindow = new NSWindow(rect, style, NSBackingStore.Buffered, false);
            mainWindow.Title = "Mission Planner on Mac!";
            mainWindow.TitleVisibility = NSWindowTitleVisibility.Hidden;
            mainWindow.DidResize += MainWindow_DidResize;
            mainWindow.WillClose += MainWindow_WillClose;
            NSEvent.AddLocalMonitorForEventsMatchingMask(NSEventMask.KeyDown, KeyboardEventHandler);

            Test.BlueToothDevice = new BTDevice();
            Test.UsbDevices = new USBDevices();
            Test.Radio = new Radio();
            Test.GPS = new GPS();
            Test.SystemInfo = new SystemInfo();
            Test.Speech = new OSXSpeech();

            WinForms.OSX = true;

            Acr.UserDialogs.Infrastructure.Log.Out += (s, s1, arg3) =>
            {
                Console.WriteLine(s + ": " + s1);
            };

            new System.Drawing.android.android();
        }

        private NSEvent KeyboardEventHandler(NSEvent keyEvent)
        {
            Console.WriteLine(keyEvent.KeyCode);

            return keyEvent;
        }

        private void MainWindow_WillClose(object sender, EventArgs e)
        {
            Console.WriteLine("MainWindow_WillClose");
            WinForms.Exit();
            System.Diagnostics.Process.GetCurrentProcess().CloseMainWindow();
        }

        private void MainWindow_DidResize(object sender, EventArgs e)
        {
            // the area the page is drawn in, which the frame's title bar and toolbar are not part of
            var size = mainWindow.ContentLayoutRect.Size;
            Console.WriteLine("MainWindow_DidResize " + size);
            WinForms.Resize((int)size.Width, (int)size.Height);
        }

        private  NSWindow mainWindow;

        public override NSWindow MainWindow { get => mainWindow; }

        public override void DidFinishLaunching(NSNotification notification)
        {
            // Insert code here to initialize your application
            Forms.Forms.Init();
            LoadApplication(new Xamarin.App());// new App());
            base.DidFinishLaunching(notification);
        }

        public override void WillTerminate(NSNotification notification)
        {
            // Insert code here to tear down your application
        }

        
        public class Radio : IRadio
        {
            public void Toggle()
            {
          
            }
        }

        public class BTDevice : IBlueToothDevice
        {
            public async Task<List<DeviceInfo>> GetDeviceInfoList()
            {
                return new List<DeviceInfo>();
            }

            public Task<ICommsSerial> GetBT(DeviceInfo first)
            {
                throw new NotImplementedException();
            }
        }

        public class USBDevices : IUSBDevices
        {
            public event EventHandler<DeviceInfo> USBEvent;

            public DeviceInfo GetDeviceInfo(object devicein)
            {
                throw new NotImplementedException();
            }

            public void USBEventCallBack(object usbDeviceReceiver, object device)
            {
                throw new NotImplementedException();
            }

            public async Task<ICommsSerial> GetUSB(DeviceInfo di)
            {
                Console.WriteLine("Looking for " + di.board);

                foreach (var device in GetUsbSerialDevices())
                {
                    if (di.board == device.Name)
                    {
                        Console.WriteLine("GetUSB: Port: " + device.Ports[0]);
                        return new SerialPort(device.Ports[0]);
                    }
                }

                return null;
            }

            async Task<List<DeviceInfo>> IUSBDevices.GetDeviceInfoList()
            {
                var ans = new List<DeviceInfo>();

                // only usb devices that have a serial port are returned
                foreach (var device in GetUsbSerialDevices())
                {
                    ans.Add(new DeviceInfo()
                    {
                        board = device.Name,
                        description = device.Name,
                        hardwareid = String.Format("USB\\VID_{0:X4}&PID_{1:X4}&", device.Vid, device.Pid),
                        name = device.Name
                    });
                }

                return ans;
            }

            private class UsbSerialDevice
            {
                public string Name;
                public int Vid;
                public int Pid;
                public List<string> Ports = new List<string>();
            }

            private static readonly Regex DeviceRegex =
                new Regex(@"\+-o\s+(.+?)@(\w+)\s+<class IOUSBHostDevice");

            private static readonly Regex PropertyRegex =
                new Regex(@"""(idVendor|idProduct|IOCalloutDevice)""\s+=\s+""?([^""]+)""?");

            /// <summary>
            /// The usb devices that have a serial port. The port names come from the device's own entry
            /// in the IO registry, as a port name cannot be derived from the usb location: it is built
            /// from the location with its zeros removed, or from the serial number.
            /// </summary>
            private static List<UsbSerialDevice> GetUsbSerialDevices()
            {
                var ans = new List<UsbSerialDevice>();
                UsbSerialDevice current = null;

                foreach (var line in GetUSBList().Split(new[] { '\r', '\n' }))
                {
                    var device = DeviceRegex.Match(line);
                    if (device.Success)
                    {
                        // a device's properties and ports follow it, up to the next device
                        var loc = device.Groups[2].Value;
                        current = new UsbSerialDevice()
                        {
                            Name = device.Groups[1].Value + "@" + loc.Substring(0, Math.Min(4, loc.Length))
                        };
                        ans.Add(current);
                        continue;
                    }

                    if (current == null)
                        continue;

                    var property = PropertyRegex.Match(line);
                    if (!property.Success)
                        continue;

                    var value = property.Groups[2].Value.Trim();
                    switch (property.Groups[1].Value)
                    {
                        case "idVendor":
                            int.TryParse(value, out current.Vid);
                            break;
                        case "idProduct":
                            int.TryParse(value, out current.Pid);
                            break;
                        case "IOCalloutDevice":
                            if (!current.Ports.Contains(value))
                                current.Ports.Add(value);
                            break;
                    }
                }

                ans = ans.Where(a => a.Ports.Count > 0).ToList();
                ans.ForEach(a => a.Ports.Sort(StringComparer.Ordinal));
                return ans;
            }

            private static string GetUSBList()
            {
                var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ioreg",
                    "-r -c IOUSBHostDevice -l -w0")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                });
                var data = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                return data;
            }
        }
    }

    public class OSXSpeech : ISpeech
    {
        private NSSpeechSynthesizer speechSynthesizer = new NSSpeechSynthesizer();

        public bool speechEnable { get; set; }
        public bool IsReady { get; set; }
        public void SpeakAsync(string text)
        {
            if (!speechEnable)
                return;
            
            _ = Task.Run(() =>
            {
                IsReady = false;
                try
                {
                    //NSRunLoop.Main.BeginInvokeOnMainThread();
                    Device.InvokeOnMainThreadAsync(() => speechSynthesizer.StartSpeakingString(text));
                }
                catch
                {
                }
                IsReady = true;
            });
        }

        public void SpeakAsyncCancelAll()
        {
            Device.InvokeOnMainThreadAsync(() => speechSynthesizer.StopSpeaking());
        }
    }
}