// TODO fill in this information for your driver, then remove this line!
//
// ASCOM Telescope hardware class for photonProxyHub
//
// Description:	 <To be completed by driver developer>
//
// Implements:	ASCOM Telescope interface version: <To be completed by driver developer>
// Author:		(XXX) Your N. Here <your@email.here>
//

using ASCOM;
using ASCOM.Astrometry;
using ASCOM.Astrometry.AstroUtils;
using ASCOM.Astrometry.NOVAS;
using ASCOM.Astrometry.Transform;
using ASCOM.DeviceInterface;
using ASCOM.LocalServer;
using ASCOM.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ASCOM.photonProxyHub.Telescope
{
    //
    // TODO Replace the not implemented exceptions with code to implement the function or throw the appropriate ASCOM exception.
    //

    /// <summary>
    /// ASCOM Telescope hardware class for photonProxyHub.
    /// </summary>
    [HardwareClass()] // Class attribute flag this as a device hardware class that needs to be disposed by the local server when it exits.
    internal static class TelescopeHardware
    {
        // Constants used for Profile persistence
        internal const string traceStateProfileName = "Trace Level";
        internal const string traceStateDefault = "true";

        internal static bool isSlewSettle = true; // Variable to hold the slew settling state
        
        private static string DriverProgId = ""; // ASCOM DeviceID (COM ProgID) for this driver, the value is set by the driver's class initialiser.
        private static string DriverDescription = ""; // The value is set by the driver's class initialiser.
        internal static string proxyDriverProgId; // COM port name (if required)
        private static bool connectedState; // Local server's connected state
        private static bool runOnce = false; // Flag to enable "one-off" activities only to run once.
        internal static Util utilities; // ASCOM Utilities object for use as required
        internal static AstroUtils astroUtilities; // ASCOM AstroUtilities object for use as required
        internal static TraceLogger tl; // Local server's trace logger object for diagnostic log with information that you specify

        // Variables to hold the currrent device configuration for this driver
        internal static ASCOM.DriverAccess.Telescope driver;
        internal static bool isMoving = false;
        internal static bool hasTargetDeclination = false;
        internal static bool hasTargetRightAscension = false;
        internal static bool _AtPark = false;

        /// <summary>
        /// Initializes a new instance of the device Hardware class.
        /// </summary>
        static TelescopeHardware()
        {
            try
            {
                // Create the hardware trace logger in the static initialiser.
                // All other initialisation should go in the InitialiseHardware method.
                tl = new TraceLogger("", "photonProxyHub.Hardware");

                // DriverProgId has to be set here because it used by ReadProfile to get the TraceState flag.
                DriverProgId = Telescope.DriverProgId; // Get this device's ProgID so that it can be used to read the Profile configuration values

                // ReadProfile has to go here before anything is written to the log because it loads the TraceLogger enable / disable state.
                ReadProfile(); // Read device configuration from the ASCOM Profile store, including the trace state

                LogMessage("TelescopeHardware", $"Static initialiser completed.");
            }
            catch (Exception ex)
            {
                try { LogMessage("TelescopeHardware", $"Initialisation exception: {ex}"); } catch { }
                MessageBox.Show($"{ex.Message}", "Exception creating ASCOM.photonProxyHub.Telescope", MessageBoxButtons.OK, MessageBoxIcon.Error);
                throw;
            }
        }

        /// <summary>
        /// Place device initialisation code here
        /// </summary>
        /// <remarks>Called every time a new instance of the driver is created.</remarks>
        internal static void InitialiseHardware()
        {
            // This method will be called every time a new ASCOM client loads your driver
            LogMessage("InitialiseHardware", $"Start.");

            // Make sure that "one off" activities are only undertaken once
            if (runOnce == false)
            {
                LogMessage("InitialiseHardware", $"Starting one-off initialisation.");

                DriverDescription = Telescope.DriverDescription; // Get this device's Chooser description

                LogMessage("InitialiseHardware", $"ProgID: {DriverProgId}, Description: {DriverDescription}");

                connectedState = false; // Initialise connected to false
                utilities = new Util(); //Initialise ASCOM Utilities object
                astroUtilities = new AstroUtils(); // Initialise ASCOM Astronomy Utilities object

                LogMessage("InitialiseHardware", "Completed basic initialisation");

                // Add your own "one off" device initialisation here e.g. validating existence of hardware and setting up communications

                LogMessage("InitialiseHardware", $"One-off initialisation complete.");
                runOnce = true; // Set the flag to ensure that this code is not run again
            }
        }

        // PUBLIC COM INTERFACE ITelescopeV3 IMPLEMENTATION

        #region Common properties and methods.

        /// <summary>
        /// Displays the Setup Dialogue form.
        /// If the user clicks the OK button to dismiss the form, then
        /// the new settings are saved, otherwise the old values are reloaded.
        /// THIS IS THE ONLY PLACE WHERE SHOWING USER INTERFACE IS ALLOWED!
        /// </summary>
        public static void SetupDialog()
        {
            // Don't permit the setup dialogue if already connected
            if (IsConnected)
                MessageBox.Show("Already connected, just press OK");

            using (SetupDialogForm F = new SetupDialogForm(tl))
            {
                var result = F.ShowDialog();
                if (result == DialogResult.OK)
                {
                    WriteProfile(); // Persist device configuration values to the ASCOM Profile store
                }
            }
        }

        /// <summary>Returns the list of custom action names supported by this driver.</summary>
        /// <value>An ArrayList of strings (SafeArray collection) containing the names of supported actions.</value>
        public static ArrayList SupportedActions
        {
            get
            {
                ArrayList result = new ArrayList();
                result.Add("Telescope:MotorOn");
                result.Add("Telescope:MotorOff");
                result.Add("Telescope:StartFans");
                result.Add("Telescope:StopFans");

                return result; //driver.SupportedActions;
            }
        }

        /// <summary>Invokes the specified device-specific custom action.</summary>
        /// <param name="ActionName">A well known name agreed by interested parties that represents the action to be carried out.</param>
        /// <param name="ActionParameters">List of required parameters or an <see cref="String.Empty">Empty String</see> if none are required.</param>
        /// <returns>A string response. The meaning of returned strings is set by the driver author.
        /// <para>Suppose filter wheels start to appear with automatic wheel changers; new actions could be <c>QueryWheels</c> and <c>SelectWheel</c>. The former returning a formatted list
        /// of wheel names and the second taking a wheel name and making the change, returning appropriate values to indicate success or failure.</para>
        /// </returns>
        public static string Action(string actionName, string actionParameters)
        {
            return driver.Action(actionName, actionParameters);

        }

        /// <summary>
        /// Transmits an arbitrary string to the device and does not wait for a response.
        /// Optionally, protocol framing characters may be added to the string before transmission.
        /// </summary>
        /// <param name="Command">The literal command string to be transmitted.</param>
        /// <param name="Raw">
        /// if set to <c>true</c> the string is transmitted 'as-is'.
        /// If set to <c>false</c> then protocol framing characters may be added prior to transmission.
        /// </param>
        public static void CommandBlind(string command, bool raw)
        {
            driver.CommandBlind(command, raw);

        }

        /// <summary>
        /// Transmits an arbitrary string to the device and waits for a boolean response.
        /// Optionally, protocol framing characters may be added to the string before transmission.
        /// </summary>
        /// <param name="Command">The literal command string to be transmitted.</param>
        /// <param name="Raw">
        /// if set to <c>true</c> the string is transmitted 'as-is'.
        /// If set to <c>false</c> then protocol framing characters may be added prior to transmission.
        /// </param>
        /// <returns>
        /// Returns the interpreted boolean response received from the device.
        /// </returns>
        public static bool CommandBool(string command, bool raw)
        {
            return driver.CommandBool(command, raw);
        }

        /// <summary>
        /// Transmits an arbitrary string to the device and waits for a string response.
        /// Optionally, protocol framing characters may be added to the string before transmission.
        /// </summary>
        /// <param name="Command">The literal command string to be transmitted.</param>
        /// <param name="Raw">
        /// if set to <c>true</c> the string is transmitted 'as-is'.
        /// If set to <c>false</c> then protocol framing characters may be added prior to transmission.
        /// </param>
        /// <returns>
        /// Returns the string response received from the device.
        /// </returns>
        public static string CommandString(string command, bool raw)
        {
            return driver.CommandString(command, raw);
        }

        /// <summary>
        /// Deterministically release both managed and unmanaged resources that are used by this class.
        /// </summary>
        /// <remarks>
        /// TODO: Release any managed or unmanaged resources that are used in this class.
        /// 
        /// Do not call this method from the Dispose method in your driver class.
        ///
        /// This is because this hardware class is decorated with the <see cref="HardwareClassAttribute"/> attribute and this Dispose() method will be called 
        /// automatically by the  local server executable when it is irretrievably shutting down. This gives you the opportunity to release managed and unmanaged 
        /// resources in a timely fashion and avoid any time delay between local server close down and garbage collection by the .NET runtime.
        ///
        /// For the same reason, do not call the SharedResources.Dispose() method from this method. Any resources used in the static shared resources class
        /// itself should be released in the SharedResources.Dispose() method as usual. The SharedResources.Dispose() method will be called automatically 
        /// by the local server just before it shuts down.
        /// 
        /// </remarks>
        public static void Dispose()
        {
            try { LogMessage("Dispose", $"Disposing of assets and closing down."); } catch { }

            try
            {
                // Clean up the trace logger and utility objects
                tl.Enabled = false;
                tl.Dispose();
                tl = null;
            }
            catch { }

            try
            {
                utilities.Dispose();
                utilities = null;
            }
            catch { }

            try
            {
                astroUtilities.Dispose();
                astroUtilities = null;
            }
            catch { }
        }

       
        /// <summary>
        /// Set True to connect to the device hardware. Set False to disconnect from the device hardware.
        /// You can also read the property to check whether it is connected. This reports the current hardware state.
        /// </summary>
        /// <value><c>true</c> if connected to the hardware; otherwise, <c>false</c>.</value>
        public static bool Connected
        {
            get
            {
                LogMessage("Connected", $"Get {IsConnected}");
                return IsConnected;
            }
            set
            {
                LogMessage("Connected", $"Set {value}");
                if (value == IsConnected)
                    return;

                if (value)
                {
                    LogMessage("Connected Set", $"Connecting to proxy {Properties.Settings.Default.proxyDriverId}");

                    // TODO insert connect to the device code here
                    driver = new ASCOM.DriverAccess.Telescope(Properties.Settings.Default.proxyDriverId);
                    driver.Connected = true;

                    connectedState = true;
                    
                    long lastConnected = Convert.ToInt32((DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds);


                    //Set slew settle time from properties, since this setting is not persistent in the driver
                    driver.SlewSettleTime = Convert.ToInt16(Properties.Settings.Default.SlewSettleTime);

                    if (Convert.ToBoolean(Properties.Settings.Default.RestorePosition))
                    {
                        try
                        {
                            long disconnected = Properties.Settings.Default.Disconnected;

                            if (lastConnected < disconnected)
                            {

                                if (Properties.Settings.Default.Az != 0 || Properties.Settings.Default.Alt != 0)
                                {
                                    double Alt = Convert.ToDouble(Properties.Settings.Default.Alt);
                                    double Az = Convert.ToDouble(Properties.Settings.Default.Az);

                                    // only sync if we have more then 0.5 degree difference
                                    if (Math.Abs(driver.Altitude - Alt) > 0.5 || Math.Abs(driver.Azimuth - Az) > 0.5)
                                    {

                                        Transform transform = new Transform();

                                        transform.SiteLatitude = TelescopeHardware.SiteLatitude; ;
                                        transform.SiteLongitude = TelescopeHardware.SiteLongitude; ;
                                        transform.JulianDateTT = 0.0;

                                        transform.SetAzimuthElevation(Az, Alt);

                                        driver.SyncToCoordinates(transform.RAApparent, transform.DECApparent);
                                        LogMessage("Connected Set", $"Restoring position to Alt={driver.Altitude} Az={driver.Azimuth}");
                                    }
                                    else
                                    {
                                        LogMessage("Connected Set", $"No position to restore, position is within tolerance");
                                    }
                                }
                            } else                             {
                                LogMessage("Connected Set", $"No position to restore, last disconnect was before last connect!");
                            }

                        } catch (Exception ex)
                        {
                            LogMessage("Connected Set", $"Error restoring position: {ex.Message}");
                        }
                    }

                    Properties.Settings.Default.Connected = Convert.ToInt32((DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds);
                    Properties.Settings.Default.Save();

                    _AtPark = driver.AtPark;
                }
                else
                {
                    //LogMessage("Connected Set", $"Disconnecting from port {comPort}");


                    Properties.Settings.Default.Az = driver.Azimuth;
                    Properties.Settings.Default.Alt = driver.Altitude;
                    Properties.Settings.Default.Disconnected = Convert.ToInt32((DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds);
                    Properties.Settings.Default.Save();
                    
                    LogMessage("Connected Set", $"Disconnecting from ASA Proxy Alt={driver.Altitude} Az={driver.Azimuth}");

                    // TODO insert disconnect from the device code here
                    driver.Connected = false;
                    connectedState = false;
                }
            }
        }

        /// <summary>
        /// Returns a description of the device, such as manufacturer and model number. Any ASCII characters may be used.
        /// </summary>
        /// <value>The description.</value>
        public static string Description
        {
            // TODO customise this device description if required
            get
            {
                return driver.Description;
            }
        }

        /// <summary>
        /// Descriptive and version information about this ASCOM driver.
        /// </summary>
        public static string DriverInfo
        {
            get
            {
                return driver.DriverInfo;
            }
        }

        /// <summary>
        /// A string containing only the major and minor version of the driver formatted as 'm.n'.
        /// </summary>
        public static string DriverVersion
        {
            get
            {
                return driver.DriverVersion;
            }
        }

        /// <summary>
        /// The interface version number that this device supports.
        /// </summary>
        public static short InterfaceVersion
        {
            // set by the driver wizard
            get
            {
                LogMessage("InterfaceVersion Get", "3");
                return Convert.ToInt16("3");
            }
        }

        /// <summary>
        /// The short name of the driver, for display purposes
        /// </summary>
        public static string Name
        {
            // TODO customise this device name as required
            get
            {
                return driver.Name;
            }
        }

        #endregion

        #region ITelescope Implementation

        /// <summary>
        /// Stops a slew in progress.
        /// </summary>
        internal static void AbortSlew()
        {
            if (AtPark)
                throw new InvalidOperationException("Cannot abort slew when parked");

            driver.AbortSlew();
        }

        /// <summary>
        /// The alignment mode of the mount (Alt/Az, Polar, German Polar).
        /// </summary>
        internal static AlignmentModes AlignmentMode
        {
            get
            {
                return driver.AlignmentMode;
            }
        }

        /// <summary>
        /// The Altitude above the local horizon of the telescope's current position (degrees, positive up)
        /// </summary>
        internal static double Altitude
        {
            get
            {
                return driver.Altitude;
            }
        }

        /// <summary>
        /// The area of the telescope's aperture, taking into account any obstructions (square meters)
        /// </summary>
        internal static double ApertureArea
        {
            get
            {
                LogMessage("ApertureArea Get", "Not implemented");
                throw new PropertyNotImplementedException("ApertureArea", false);
            }
        }

        /// <summary>
        /// The telescope's effective aperture diameter (meters)
        /// </summary>
        internal static double ApertureDiameter
        {
            get
            {
                LogMessage("ApertureDiameter Get", "Not implemented");
                throw new PropertyNotImplementedException("ApertureDiameter", false);
            }
        }

        /// <summary>
        /// True if the telescope is stopped in the Home position. Set only following a <see cref="FindHome"></see> operation,
        /// and reset with any slew operation. This property must be False if the telescope does not support homing.
        /// </summary>
        internal static bool AtHome
        {
            get
            {
                return driver.AtHome;
            }
        }

        /// <summary>
        /// True if the telescope has been put into the parked state by the seee <see cref="Park" /> method. Set False by calling the Unpark() method.
        /// </summary>
        internal static bool AtPark
        {
            get
            {
                return _AtPark;

                /*
                if (!driver.AtPark && _AtPark)
                {
                    return _AtPark;
                }
                return driver.AtPark;
                */


            }
        }

        /// <summary>
        /// Determine the rates at which the telescope may be moved about the specified axis by the <see cref="MoveAxis" /> method.
        /// </summary>
        /// <param name="Axis">The axis about which rate information is desired (TelescopeAxes value)</param>
        /// <returns>Collection of <see cref="IRate" /> rate objects</returns>
        internal static IAxisRates AxisRates(TelescopeAxes Axis)
        {
            LogMessage("AxisRates", "Get - " + Axis.ToString());
            return new AxisRates(Axis);
        }

        /// <summary>
        /// The azimuth at the local horizon of the telescope's current position (degrees, North-referenced, positive East/clockwise).
        /// </summary>
        internal static double Azimuth
        {
            get
            {
                return driver.Azimuth;

            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed finding its home position (<see cref="FindHome" /> method).
        /// </summary>
        internal static bool CanFindHome
        {
            get
            {
                return driver.CanFindHome;
            }
        }

        /// <summary>
        /// True if this telescope can move the requested axis
        /// </summary>
        internal static bool CanMoveAxis(TelescopeAxes Axis)
        {
            return driver.CanMoveAxis(Axis);
        }

        /// <summary>
        /// True if this telescope is capable of programmed parking (<see cref="Park" />method)
        /// </summary>
        internal static bool CanPark
        {
            get
            {
                return driver.CanPark;
            }
        }

        /// <summary>
        /// True if this telescope is capable of software-pulsed guiding (via the <see cref="PulseGuide" /> method)
        /// </summary>
        internal static bool CanPulseGuide
        {
            get
            {
                return driver.CanPulseGuide;
            }
        }

        /// <summary>
        /// True if the <see cref="DeclinationRate" /> property can be changed to provide offset tracking in the declination axis.
        /// </summary>
        internal static bool CanSetDeclinationRate
        {
            get
            {
                return driver.CanSetDeclinationRate;
            }
        }

        /// <summary>
        /// True if the guide rate properties used for <see cref="PulseGuide" /> can ba adjusted.
        /// </summary>
        internal static bool CanSetGuideRates
        {
            get
            {
                return driver.CanSetGuideRates;
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed setting of its park position (<see cref="SetPark" /> method)
        /// </summary>
        internal static bool CanSetPark
        {
            get
            {
                return driver.CanSetPark;
            }
        }

        /// <summary>
        /// True if the <see cref="SideOfPier" /> property can be set, meaning that the mount can be forced to flip.
        /// </summary>
        internal static bool CanSetPierSide
        {
            get
            {
                return driver.CanSetPark;
            }
        }

        /// <summary>
        /// True if the <see cref="RightAscensionRate" /> property can be changed to provide offset tracking in the right ascension axis.
        /// </summary>
        internal static bool CanSetRightAscensionRate
        {
            get
            {
                return driver.CanSetRightAscensionRate;
            }
        }

        /// <summary>
        /// True if the <see cref="Tracking" /> property can be changed, turning telescope sidereal tracking on and off.
        /// </summary>
        internal static bool CanSetTracking
        {
            get
            {
                return driver.CanSetTracking;
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed slewing (synchronous or asynchronous) to equatorial coordinates
        /// </summary>
        internal static bool CanSlew
        {
            get
            {
                return driver.CanSlew;
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed slewing (synchronous or asynchronous) to local horizontal coordinates
        /// </summary>
        internal static bool CanSlewAltAz
        {
            get
            {
                return driver.CanSlewAltAz;
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed asynchronous slewing to local horizontal coordinates
        /// </summary>
        internal static bool CanSlewAltAzAsync
        {
            get
            {
                return driver.CanSlewAltAzAsync;
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed asynchronous slewing to equatorial coordinates.
        /// </summary>
        internal static bool CanSlewAsync
        {
            get
            {
                return driver.CanSlewAsync;
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed synching to equatorial coordinates.
        /// </summary>
        internal static bool CanSync
        {
            get
            {
                return driver.CanSync;
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed synching to local horizontal coordinates
        /// </summary>
        internal static bool CanSyncAltAz
        {
            get
            {
                return driver.CanSyncAltAz;
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed unparking (<see cref="Unpark" /> method).
        /// </summary>
        internal static bool CanUnpark
        {
            get
            {
                // if (!driver.AtPark)
                //     return false;

                return driver.CanUnpark;
            }
        }

        /// <summary>
        /// The declination (degrees) of the telescope's current equatorial coordinates, in the coordinate system given by the <see cref="EquatorialSystem" /> property.
        /// Reading the property will raise an error if the value is unavailable.
        /// </summary>
        internal static double Declination
        {
            get
            {
                return driver.Declination;
            }
        }

        /// <summary>
        /// The declination tracking rate (arcseconds per SI second, default = 0.0)
        /// </summary>
        internal static double DeclinationRate
        {
            get
            {
                /* It seems that the ASA driver includes also the correction */

                return driver.DeclinationRate; // - driver.pointingCorrectionDE;
            }
            set
            {
                if (value < -1.0 || value > 1.0)
                    throw new InvalidValueException("DeclinationRate", value.ToString(), "-1.0 to +1.0");
                driver.DeclinationRate = value;
            }
        }

        /// <summary>
        /// Predict side of pier for German equatorial mounts at the provided coordinates
        /// </summary>
        internal static PierSide DestinationSideOfPier(double RightAscension, double Declination)
        {
            return driver.DestinationSideOfPier(RightAscension, Declination);
        }

        /// <summary>
        /// True if the telescope or driver applies atmospheric refraction to coordinates.
        /// </summary>
        internal static bool DoesRefraction
        {
            get
            {
                return driver.DoesRefraction;
            }
            set
            {
                driver.DoesRefraction = value;
            }
        }

        /// <summary>
        /// Equatorial coordinate system used by this telescope (e.g. Topocentric or J2000).
        /// </summary>
        internal static EquatorialCoordinateType EquatorialSystem
        {
            get
            {
                return driver.EquatorialSystem;
            }
        }

        /// <summary>
        /// Locates the telescope's "home" position (synchronous)
        /// </summary>
        internal static void FindHome()
        {
            if (AtPark)
                throw new InvalidOperationException("Cannot find home when parked");

            driver.FindHome();

        }

        /// <summary>
        /// The telescope's focal length, meters
        /// </summary>
        internal static double FocalLength
        {
            get
            {
                LogMessage("FocalLength Get", "Not implemented");
                throw new PropertyNotImplementedException("FocalLength", false);
            }
        }

        /// <summary>
        /// The current Declination movement rate offset for telescope guiding (degrees/sec)
        /// </summary>
        internal static double GuideRateDeclination
        {
            get
            {
                LogMessage("GuideRateDeclination Get", "Not implemented");
                throw new PropertyNotImplementedException("GuideRateDeclination", false);
            }
            set
            {
                LogMessage("GuideRateDeclination Set", "Not implemented");
                throw new PropertyNotImplementedException("GuideRateDeclination", true);
            }
        }

        /// <summary>
        /// The current Right Ascension movement rate offset for telescope guiding (degrees/sec)
        /// </summary>
        internal static double GuideRateRightAscension
        {
            get
            {
                LogMessage("GuideRateRightAscension Get", "Not implemented");
                throw new PropertyNotImplementedException("GuideRateRightAscension", false);
            }
            set
            {
                LogMessage("GuideRateRightAscension Set", "Not implemented");
                throw new PropertyNotImplementedException("GuideRateRightAscension", true);
            }
        }

        /// <summary>
        /// True if a <see cref="PulseGuide" /> command is in progress, False otherwise
        /// </summary>
        internal static bool IsPulseGuiding
        {
            get
            {
                return (bool)driver.IsPulseGuiding;
            }
        }

        /// <summary>
        /// Move the telescope in one axis at the given rate.
        /// </summary>
        /// <param name="Axis">The physical axis about which movement is desired</param>
        /// <param name="Rate">The rate of motion (deg/sec) about the specified axis</param>
        internal static void MoveAxis(TelescopeAxes Axis, double Rate)
        {
            if (AtPark)
            {
                throw new InvalidOperationException("Cannot move when parked");
            }
            double maxSlewRate = Convert.ToDouble(Properties.Settings.Default.maxSlewRate);
            if (Rate > maxSlewRate || Rate <(maxSlewRate*-1))
            {
                throw new InvalidValueException("MoveAxis", Rate.ToString(), $"-{maxSlewRate} to +{maxSlewRate}");
            }
            driver.MoveAxis(Axis, Rate);
            if (Rate == 0) isMoving = false;
            else
                isMoving = true;
        }


        /// <summary>
        /// Move the telescope to its park position, stop all motion (or restrict to a small safe range), and set <see cref="AtPark" /> to True.
        /// </summary>
        internal static void Park()
        {
            driver.Park();
            _AtPark = true;
        }

        /// <summary>
        /// Moves the scope in the given direction for the given interval or time at
        /// the rate given by the corresponding guide rate property
        /// </summary>
        /// <param name="Direction">The direction in which the guide-rate motion is to be made</param>
        /// <param name="Duration">The duration of the guide-rate motion (milliseconds)</param>
        internal static void PulseGuide(GuideDirections Direction, int Duration)
        {
            if (AtPark)
            {
                throw new InvalidOperationException("Cannot PulseGuide when parked");
            }
            driver.PulseGuide(Direction, Duration);
        }

        /// <summary>
        /// The right ascension (hours) of the telescope's current equatorial coordinates,
        /// in the coordinate system given by the EquatorialSystem property
        /// </summary>
        internal static double RightAscension
        {
            get
            {
                return driver.RightAscension;
            }
        }

        /// <summary>
        /// The right ascension tracking rate offset from sidereal (seconds per sidereal second, default = 0.0)
        /// </summary>
        internal static double RightAscensionRate
        {
            get
            {
                return driver.RightAscensionRate; // - driver.pointingCorrectionRA;
            }
            set
            {
                if (value < -1.0 || value > 1.0)
                    throw new InvalidValueException("RightAscensionRate", value.ToString(), "-1.0 to +1.0");
                driver.RightAscensionRate = value;
            }
        }

        /// <summary>
        /// Sets the telescope's park position to be its current position.
        /// </summary>
        internal static void SetPark()
        {
            driver.SetPark();
            _AtPark = true;
        }

        /// <summary>
        /// Indicates the pointing state of the mount. Read the articles installed with the ASCOM Developer
        /// Components for more detailed information.
        /// </summary>
        internal static PierSide SideOfPier
        {
            get
            {
                return driver.SideOfPier;
            }
            set
            {
                if (value != PierSide.pierEast && value != PierSide.pierWest)
                    throw new InvalidValueException("SideOfPier", value.ToString(), "PierEast or PierWest");

                driver.SideOfPier = value;
            }
        }

        /// <summary>
        /// The local apparent sidereal time from the telescope's internal clock (hours, sidereal)
        /// </summary>
        internal static double SiderealTime
        {
            get
            {
                return driver.SiderealTime;
            }
        }

        /// <summary>
        /// The elevation above mean sea level (meters) of the site at which the telescope is located
        /// </summary>
        internal static double SiteElevation
        {
            get
            {
                return driver.SiteElevation;
            }
            set
            {
                if (value < -300 || value > 10000)
                    throw new InvalidValueException("SiteElevation", value.ToString(), "-300 to +10000");
                driver.SiteElevation = value;
            }
        }

        /// <summary>
        /// The geodetic(map) latitude (degrees, positive North, WGS84) of the site at which the telescope is located.
        /// </summary>
        internal static double SiteLatitude
        {
            get
            {
                return driver.SiteLatitude;
            }
            set
            {
                if (value < -90 || value > 90)
                    throw new InvalidValueException("SiteLatitude", value.ToString(), "-90 to +90");
                driver.SiteLatitude = value;
            }
        }

        /// <summary>
        /// The longitude (degrees, positive East, WGS84) of the site at which the telescope is located.
        /// </summary>
        internal static double SiteLongitude
        {
            get
            {
                return driver.SiteLongitude;
            }
            set
            {
                if (value < -180 || value > 180)
                    throw new InvalidValueException("SiteLongitude", value.ToString(), "-180 to +180");
                driver.SiteLongitude = value;

            }
        }

        /// <summary>
        /// Specifies a post-slew settling time (sec.).
        /// </summary>
        internal static short SlewSettleTime
        {
            get
            {
                if (isSlewSettle)
                { return Convert.ToInt16(Properties.Settings.Default.SlewSettleTime); ; }
                else
                {
                    return (short)driver.SlewSettleTime;
                }

            }
            set
            {
                if (value < 0 || value > 600)
                    throw new InvalidValueException("SlewSettleTime", value.ToString(), "0 to 600");

                if (isSlewSettle)
                {
                    Properties.Settings.Default.SlewSettleTime = value.ToString();

                }
                else
                {
                    driver.SlewSettleTime = value;
                }
            }
        }



        /// <summary>
        /// Move the telescope to the given local horizontal coordinates, return when slew is complete
        /// </summary>
        internal static void SlewToAltAz(double Azimuth, double Altitude)
        {
            if (Azimuth < 0 || Azimuth >= 360)
                throw new InvalidValueException("Azimuth", Azimuth.ToString(), "0 to 360 degrees");
            if (Altitude < -90 || Altitude > 90)
                throw new InvalidValueException("Altitude", Altitude.ToString(), "-90 to +90 degrees");
            driver.SlewToAltAz(Azimuth, Altitude);
            Settle();
        }


        /// <summary>
        /// This Method must be implemented if <see cref="CanSlewAltAzAsync" /> returns True.
        /// It returns immediately, with Slewing set to True
        /// </summary>
        /// <param name="Azimuth">Azimuth to which to move</param>
        /// <param name="Altitude">Altitude to which to move to</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "VSTHRD200:Use \"Async\" suffix for async methods", Justification = "internal static method name used for many years.")]
        internal static void SlewToAltAzAsync(double Azimuth, double Altitude)
        {
            if (Azimuth < 0 || Azimuth >= 360)
                throw new InvalidValueException("Azimuth", Azimuth.ToString(), "0 to 360 degrees");
            if (Altitude < -90 || Altitude > 90)
                throw new InvalidValueException("Altitude", Altitude.ToString(), "-90 to +90 degrees");
            driver.SlewToAltAzAsync(Azimuth, Altitude);
        }

        internal static void ValidateCoordinates(double RightAscension, double Declination)
        {
            if (RightAscension < 0 || RightAscension >= 24)
                throw new InvalidValueException("RightAscension", RightAscension.ToString(), "0 to 24 hours");
            if (Declination < -90 || Declination > 90)
                throw new InvalidValueException("Declination", Declination.ToString(), "-90 to +90 degrees");
        }
        /// <summary>
        /// This Method must be implemented if <see cref="CanSlewAltAzAsync" /> returns True.
        /// It does not return to the caller until the slew is complete.
        /// </summary>
        internal static void SlewToCoordinates(double RightAscension, double Declination)
        {
            if (AtPark)
            {
                throw new InvalidOperationException("Cannot SlewToCoordinates when parked");
            }
            ValidateCoordinates(RightAscension, Declination);
            driver.SlewToCoordinates(RightAscension, Declination);
            Settle();
        }

        /// <summary>
        /// Move the telescope to the given equatorial coordinates, return with Slewing set to True immediately after starting the slew.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "VSTHRD200:Use \"Async\" suffix for async methods", Justification = "internal static method name used for many years.")]
        internal static void SlewToCoordinatesAsync(double RightAscension, double Declination)
        {
            if (AtPark)
            {
                throw new InvalidOperationException("Cannot SlewToCoordinatesAsync when parked");
            }
            ValidateCoordinates(RightAscension, Declination);
            driver.SlewToCoordinatesAsync(RightAscension, Declination);
        }

        /// <summary>
        /// Move the telescope to the <see cref="TargetRightAscension" /> and <see cref="TargetDeclination" /> coordinates, return when slew complete.
        /// </summary>
        internal static void SlewToTarget()
        {
            if (AtPark)
            {
                throw new InvalidOperationException("Cannot slew when parked");
            }
            driver.SlewToTarget();
            Settle();
        }

        /// <summary>
        /// Move the telescope to the <see cref="TargetRightAscension" /> and <see cref="TargetDeclination" />  coordinates,
        /// returns immediately after starting the slew with Slewing set to True.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "VSTHRD200:Use \"Async\" suffix for async methods", Justification = "internal static method name used for many years.")]
        internal static void SlewToTargetAsync()
        {
            if (AtPark)
            {
                throw new InvalidOperationException("Cannot slew when parked");
            }
            driver.SlewToTargetAsync();
        }

        /// <summary>
        /// True if telescope is in the process of moving in response to one of the
        /// Slew methods or the <see cref="MoveAxis" /> method, False at all other times.
        /// </summary>
        internal static bool Slewing
        {
            get
            {
                return driver.Slewing || isMoving;
            }
        }

        /// <summary>
        /// Matches the scope's local horizontal coordinates to the given local horizontal coordinates.
        /// </summary>
        internal static void SyncToAltAz(double Azimuth, double Altitude)
        {
            LogMessage("SyncToAltAz", "Not implemented");
            throw new MethodNotImplementedException("SyncToAltAz");
        }

        /// <summary>
        /// Matches the scope's equatorial coordinates to the given equatorial coordinates.
        /// </summary>
        internal static void SyncToCoordinates(double RightAscension, double Declination)
        {
            if (AtPark)
            {
                throw new InvalidOperationException("Cannot sync when parked");
            }
            ValidateCoordinates(RightAscension, Declination);
            driver.SyncToCoordinates(RightAscension, Declination);
            Settle();
        }

        /// <summary>
        /// Matches the scope's equatorial coordinates to the target equatorial coordinates.
        /// </summary>
        internal static void SyncToTarget()
        {
            if (AtPark)
            {
                throw new InvalidOperationException("Cannot sync when parked");
            }
            driver.SyncToTarget();
            Settle();
        }

        /// <summary>
        /// The declination (degrees, positive North) for the target of an equatorial slew or sync operation
        /// </summary>
        internal static double TargetDeclination
        {
            get
            {
                
                if (!hasTargetDeclination)
                    throw new InvalidOperationException("TargetDeclination is not set");

                return driver.TargetDeclination;
            }
            set
            {                
                if (value < -90 || value > 90)
                    throw new InvalidValueException("TargetDeclination", value.ToString(), "-90 to +90 degrees");
                driver.TargetDeclination = value;
                hasTargetDeclination = true;
            }
        }

        /// <summary>
        /// The right ascension (hours) for the target of an equatorial slew or sync operation
        /// </summary>
        internal static double TargetRightAscension
        {
            get
            {
                if (!hasTargetRightAscension)
                    throw new InvalidOperationException("TargetRightAscension is not set");

                return driver.TargetRightAscension;
            }
            set
            {
                if (value < 0 || value >= 24)
                    throw new InvalidValueException("TargetRightAscension", value.ToString(), "0 to 24 hours");
                driver.TargetRightAscension = value;
                hasTargetRightAscension = true;
            }
        }

        /// <summary>
        /// The state of the telescope's sidereal tracking drive.
        /// </summary>
        internal static bool Tracking
        {
            get
            {
                return driver.Tracking;
            }
            set
            {
                if (value == driver.Tracking)
                    return;
                driver.Tracking = value;
                isMoving = false; //reset isMoving when mount is set to Tracking speed
            }
        }

        /// <summary>
        /// The current tracking rate of the telescope's sidereal drive
        /// </summary>
        internal static DriveRates TrackingRate
        {
            get
            {
                const DriveRates DEFAULT_DRIVERATE = DriveRates.driveSidereal;
                LogMessage("TrackingRate Get", $"{DEFAULT_DRIVERATE}");
                return DEFAULT_DRIVERATE;
            }
            set
            {
                if (value < 0 || Convert.ToInt16(value) > 4)
                    throw new InvalidValueException("TrackingRate", value.ToString(), "invalid");
                LogMessage("TrackingRate Set", "Not implemented");
                //throw new PropertyNotImplementedException("TrackingRate", true);

                /*
                 * TODO Ignore this
                 */
            }
        }

        /// <summary>
        /// Returns a collection of supported <see cref="DriveRates" /> values that describe the permissible
        /// values of the <see cref="TrackingRate" /> property for this telescope type.
        /// </summary>
        internal static ITrackingRates TrackingRates
        {
            get
            {
                ITrackingRates trackingRates = new TrackingRates();
                LogMessage("TrackingRates", "Get - ");
                foreach (DriveRates driveRate in trackingRates)
                {
                    LogMessage("TrackingRates", "Get - " + driveRate.ToString());
                }
                return trackingRates;
            }
        }

        /// <summary>
        /// The UTC date/time of the telescope's internal clock
        /// </summary>
        internal static DateTime UTCDate
        {
            get
            {
                DateTime utcDate = DateTime.UtcNow;
                LogMessage("UTCDate", "Get - " + String.Format("MM/dd/yy HH:mm:ss", utcDate));
                return utcDate;
            }
            set
            {
                LogMessage("UTCDate Set", "Not implemented");
                throw new PropertyNotImplementedException("UTCDate", true);
            }
        }

        /// <summary>
        /// Takes telescope out of the Parked state.
        /// </summary>
        internal static void Unpark()
        {
            if (AtPark)
            { 
                driver.Unpark();
                _AtPark = false;
            }

        }

        #endregion

        #region Private properties and methods
        // Useful methods that can be used as required to help with driver development

        /// <summary>
        /// Returns true if there is a valid connection to the driver hardware
        /// </summary>
        private static bool IsConnected
        {
            get
            {
                return connectedState;
            }
        }

        /// <summary>
        /// Use this function to throw an exception if we aren't connected to the hardware
        /// </summary>
        /// <param name="message"></param>
        private static void CheckConnected(string message)
        {
            if (!IsConnected)
            {
                throw new NotConnectedException(message);
            }
        }

        /// <summary>
        /// Read the device configuration from the ASCOM Profile store
        /// </summary>
        internal static void ReadProfile()
        {
            using (Profile driverProfile = new Profile())
            {
                driverProfile.DeviceType = "Telescope";
                tl.Enabled = Convert.ToBoolean(driverProfile.GetValue(DriverProgId, traceStateProfileName, string.Empty, traceStateDefault));
                proxyDriverProgId = driverProfile.GetValue(DriverProgId, "DriverProgId", string.Empty, "");
            }
        }

        /// <summary>
        /// Write the device configuration to the  ASCOM  Profile store
        /// </summary>
        internal static void WriteProfile()
        {
            using (Profile driverProfile = new Profile())
            {
                driverProfile.DeviceType = "Telescope";
                driverProfile.WriteValue(DriverProgId, traceStateProfileName, tl.Enabled.ToString());
                driverProfile.WriteValue(DriverProgId, "DriverProgId", proxyDriverProgId);
            }
        }

        /// <summary>
        /// Log helper function that takes identifier and message strings
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="message"></param>
        internal static void LogMessage(string identifier, string message)
        {
            tl.LogMessageCrLf(identifier, message);
        }

        /// <summary>
        /// Log helper function that takes formatted strings and arguments
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="message"></param>
        /// <param name="args"></param>
        internal static void LogMessage(string identifier, string message, params object[] args)
        {
            var msg = string.Format(message, args);
            LogMessage(identifier, msg);
        }

        internal static void Settle()
        {
            Thread.Sleep(1000);            
        }
        #endregion
    }
}

