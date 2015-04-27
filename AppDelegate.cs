using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using CoreBluetooth;
using CoreFoundation;
using Foundation;
using UIKit;

using MonoTouch.Dialog;

namespace BluetoothExplorer
{
	[Register ("AppDelegate")]
	public class AppDelegate : UIApplicationDelegate
	{
		UIWindow window;
		DialogViewController dvc;
		BleTransport ble;

		RootElement root_element;
		Section section_devices;

		public override bool FinishedLaunching (UIApplication application, NSDictionary launchOptions)
		{
			dvc = new DialogViewController (
				(root_element = new RootElement ("Devices")
				{
					(section_devices = new Section ("Devices")
					{
						new StringElement ("Searching..."),
					}),
				})
			);

			window = new UIWindow (UIScreen.MainScreen.Bounds);
			window.RootViewController = dvc;
			window.MakeKeyAndVisible ();

			ble = new BleTransport ();
			ble.SomethingHappened += SomethingHappened;
			ble.Start ();
			return true;
		}

		void SomethingHappened ()
		{
			section_devices.Clear ();
			section_devices.Add (new StringElement (ble.State));

			while (root_element.Count > 1)
				root_element.RemoveAt (1);
			root_element.TableView.BeginUpdates ();
			foreach (var device in ble.Devices.Values) {
				var section = new Section (device.Name);
				section.Add (new StringElement (device.State));
				section.Add (new StringElement (device.Uuid));

				if (device.Services != null) {
					foreach (var service in device.Services) {
						section.Add (new StringElement (string.Format ("Service: {0}", service.Name)));
					}
				}

				root_element.Add (section);
			}
			root_element.TableView.EndUpdates ();
		}
	}

	class BleCharacteristic {
		public string Name;
	}

	class BleService {
		public string Name;
		public List<BleCharacteristic> Characteristics = new List<BleCharacteristic> (); 
	}

	class BleDevice {
		public string Uuid;
		public string Name;
		public string State;

		public List<BleService> Services = new List<BleService> ();
		public List<BleCharacteristic> Characteristics = new List<BleCharacteristic> (); 
	}

	class BleTransport
	{
		CBCentralManager manager;

		Dictionary<string, BleDevice> devices = new Dictionary<string, BleDevice> ();

		public string State { get; private set; }
		public IDictionary<string, BleDevice> Devices { get { return devices; } }

		public delegate void SomethingHappenedHandler ();
		public event SomethingHappenedHandler SomethingHappened;

		public void Start ()
		{
			if (manager != null)
				throw new InvalidOperationException ();
			
			manager = new CBCentralManager (DispatchQueue.MainQueue);

			manager.UpdatedState += UpdatedState;
			manager.DiscoveredPeripheral += DiscoveredPeripheral;
			manager.ConnectedPeripheral += ConnectedPeripheral;
			manager.FailedToConnectPeripheral += FailedToConnectPeripheral;
			manager.DisconnectedPeripheral += DisconnectedPeripheral;
		}

		void OnSomethingHappened ()
		{
			if (SomethingHappened != null)
				SomethingHappened ();
		}

		void UpdatedState (object sender, EventArgs e)
		{
			switch (manager.State) {
			case CBCentralManagerState.PoweredOn:
				State = "Scanning...";
				manager.ScanForPeripherals ((CBUUID[]) null);
				break;
			case CBCentralManagerState.PoweredOff:
			case CBCentralManagerState.Resetting:
			case CBCentralManagerState.Unauthorized:
			case CBCentralManagerState.Unknown:
			case CBCentralManagerState.Unsupported:
				State = "Waiting for Bluetooth PowerOn...";
				manager.StopScan ();
				break;
			default:
				State = "<unknown state>";
				break;
			}

			OnSomethingHappened ();
		}

		void DiscoveredPeripheral (object sender, CBDiscoveredPeripheralEventArgs e)
		{
			var peripheral = e.Peripheral;

			var device = new BleDevice () {
				Uuid = peripheral.Identifier.ToString (),
				Name = peripheral.Name,
				State = "Connecting...",
			};
			devices.Add (device.Uuid, device);

			manager.ConnectPeripheral (peripheral, new PeripheralConnectionOptions ());

			OnSomethingHappened ();
		}

		void DisconnectedPeripheral (object sender, CBPeripheralErrorEventArgs e)
		{
			var peripheral = e.Peripheral;
			var ble_peripheral = devices [peripheral.Identifier.ToString ()];

			ble_peripheral.State = "Disconnected";

			OnSomethingHappened ();
		}

		void FailedToConnectPeripheral (object sender, CBPeripheralErrorEventArgs e)
		{
			var peripheral = e.Peripheral;
			var ble_peripheral = devices [peripheral.Identifier.ToString ()];

			ble_peripheral.State = "Failed to connect";

			OnSomethingHappened ();
		}

		void ConnectedPeripheral (object sender, CBPeripheralEventArgs e)
		{
			var peripheral = e.Peripheral;
			var ble_peripheral = devices [peripheral.Identifier.ToString ()];

			ble_peripheral.State = "Connected";

			Console.WriteLine ("ConnectedPeripheral: {0}", peripheral.Name);

			peripheral.DiscoveredService += Peripheral_DiscoveredServices;
			peripheral.DiscoveredCharacteristic += Peripheral_DiscoveredCharacteristic;
			peripheral.DiscoverServices ();

			OnSomethingHappened ();
		}

		void Peripheral_DiscoveredCharacteristic (object sender, CBServiceEventArgs e)
		{
			var peripheral = (CBPeripheral) sender;
			var ble_peripheral = devices [peripheral.Identifier.ToString ()];

			ble_peripheral.Characteristics.Add (new BleCharacteristic () { Name = "?" });

			OnSomethingHappened ();
		}

		void Peripheral_DiscoveredServices (object sender, NSErrorEventArgs e)
		{
			var peripheral = (CBPeripheral) sender;
			var ble_peripheral = devices [peripheral.Identifier.ToString ()];

			foreach (var service in peripheral.Services) {
				var ble_service = new BleService () {
					Name = service.ToString (),
				};
				if (service.Characteristics != null) {
					foreach (var characteristic in service.Characteristics) {
						ble_service.Characteristics.Add (new BleCharacteristic () {
							Name = characteristic.ToString (),
						});
					}
				}
				ble_peripheral.Services.Add (ble_service);
			}

			OnSomethingHappened ();
		}
	}
}


