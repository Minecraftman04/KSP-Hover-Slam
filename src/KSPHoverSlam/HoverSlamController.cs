using System;
using System.Globalization;
using UnityEngine;

namespace KSPHoverSlam
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class HoverSlamController : MonoBehaviour
    {
        private const double G0 = 9.80665;
        private const double RadToDeg = 180.0 / Math.PI;
        private const double DegToRad = Math.PI / 180.0;

        private enum GuidanceMode
        {
            FollowTrajectory,
            GuidedTarget
        }

        private enum FlightPhase
        {
            Idle,
            Deorbit,
            Coast,
            Correcting,
            SuicideBurn,
            FinalLanding,
            Landed,
            Aborted
        }

        private struct ImpactSolution
        {
            public bool HasImpact;
            public double Latitude;
            public double Longitude;
            public double TimeToImpact;
            public double Altitude;
        }

        private Rect _windowRect = new Rect(280f, 80f, 380f, 560f);
        private bool _windowVisible = true;
        private bool _armed;
        private bool _autoDeorbit = true;
        private bool _deployGear = true;
        private bool _enableSteering = true;
        private bool _pickTarget;
        private bool _hasTarget;
        private bool _gearCommanded;

        private GuidanceMode _mode = GuidanceMode.FollowTrajectory;
        private FlightPhase _phase = FlightPhase.Idle;
        private Vessel _vessel;

        private double _targetLat;
        private double _targetLon;
        private string _targetLatText = "0";
        private string _targetLonText = "0";

        private double _touchdownSpeed = 1.5;
        private double _burnMargin = 1.08;
        private double _gearDeployAltitude = 350.0;
        private double _deorbitThrottle = 0.18;
        private double _maxTargetSteer = 0.32;
        private double _targetDeadband = 35.0;

        private float _commandThrottle;
        private float _commandPitch;
        private float _commandYaw;
        private float _commandRoll;

        private ImpactSolution _predictedImpact;
        private double _radarAlt;
        private double _verticalSpeed;
        private double _horizontalSpeed;
        private double _targetDistance;
        private double _burnStartAltitude;
        private double _availableTwr;
        private string _lastMessage = "Ready";

        private void Start()
        {
            AttachToActiveVessel();
        }

        private void OnDestroy()
        {
            DetachFromVessel();
        }

        private void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight)
            {
                return;
            }

            if ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.H))
            {
                _windowVisible = !_windowVisible;
            }

            if (_pickTarget)
            {
                HandleTargetPick();
            }
        }

        private void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight)
            {
                return;
            }

            AttachToActiveVessel();
            UpdateGuidance();
        }

        private void OnGUI()
        {
            if (!_windowVisible || !HighLogic.LoadedSceneIsFlight)
            {
                return;
            }

            _windowRect = GUILayout.Window(GetInstanceID(), _windowRect, DrawWindow, "KSP Hover Slam");
        }

        private void AttachToActiveVessel()
        {
            Vessel active = FlightGlobals.ActiveVessel;
            if (ReferenceEquals(active, _vessel))
            {
                return;
            }

            DetachFromVessel();
            _vessel = active;

            if (_vessel != null)
            {
                _vessel.OnFlyByWire += OnFlyByWire;
            }
        }

        private void DetachFromVessel()
        {
            if (_vessel != null)
            {
                _vessel.OnFlyByWire -= OnFlyByWire;
                _vessel = null;
            }
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.Label("Status: " + _phase + " - " + _lastMessage);
            GUILayout.Label("Toggle window: Alt+H");

            GUILayout.Space(6f);
            GUILayout.Label("Guidance mode");
            if (GUILayout.Toggle(_mode == GuidanceMode.FollowTrajectory, "Follow trajectory / current impact", "Button"))
            {
                _mode = GuidanceMode.FollowTrajectory;
            }

            if (GUILayout.Toggle(_mode == GuidanceMode.GuidedTarget, "Guided target lat/lon", "Button"))
            {
                _mode = GuidanceMode.GuidedTarget;
            }

            GUILayout.Space(6f);
            GUILayout.Label("Target");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Lat", GUILayout.Width(35f));
            _targetLatText = GUILayout.TextField(_targetLatText, GUILayout.Width(110f));
            GUILayout.Label("Lon", GUILayout.Width(35f));
            _targetLonText = GUILayout.TextField(_targetLonText, GUILayout.Width(110f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Use lat/lon"))
            {
                ApplyLatLonTarget();
            }

            if (GUILayout.Button("Use vessel pos"))
            {
                CaptureCurrentPosition();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Use impact"))
            {
                CapturePredictedImpact();
            }

            if (GUILayout.Button(_pickTarget ? "Cancel pick" : "Pick under mouse"))
            {
                _pickTarget = !_pickTarget;
                _lastMessage = _pickTarget ? "Click the body to choose a target" : "Target picking cancelled";
            }
            GUILayout.EndHorizontal();

            GUILayout.Label(_hasTarget
                ? string.Format(CultureInfo.InvariantCulture, "Target: {0:0.000000}, {1:0.000000}", _targetLat, _targetLon)
                : "Target: none");

            GUILayout.Space(6f);
            _autoDeorbit = GUILayout.Toggle(_autoDeorbit, "Auto-deorbit assist");
            _deployGear = GUILayout.Toggle(_deployGear, "Deploy landing gear");
            _enableSteering = GUILayout.Toggle(_enableSteering, "Steer with grid fins / control surfaces");

            GUILayout.Space(6f);
            _touchdownSpeed = Slider("Touchdown speed", _touchdownSpeed, 0.5, 5.0, "0.0 m/s");
            _burnMargin = Slider("Burn margin", _burnMargin, 1.00, 1.35, "0.00x");
            _gearDeployAltitude = Slider("Gear deploy", _gearDeployAltitude, 50.0, 1000.0, "0 m");
            _deorbitThrottle = Slider("Deorbit throttle", _deorbitThrottle, 0.05, 0.75, "0%");
            _maxTargetSteer = Slider("Target steer", _maxTargetSteer, 0.05, 0.60, "0.00");

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_armed ? "Disarm" : "Arm hover slam"))
            {
                if (_armed)
                {
                    Abort("Disarmed");
                }
                else
                {
                    Arm();
                }
            }

            if (GUILayout.Button("Abort"))
            {
                Abort("Manual abort");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Telemetry");
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "Radar alt: {0:0} m    VSpeed: {1:0.0} m/s", _radarAlt, _verticalSpeed));
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "HSpeed: {0:0.0} m/s    TWR: {1:0.00}", _horizontalSpeed, _availableTwr));
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "Burn starts near: {0:0} m", _burnStartAltitude));

            if (_predictedImpact.HasImpact)
            {
                GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "Impact: {0:0.000}, {1:0.000} in {2:0}s", _predictedImpact.Latitude, _predictedImpact.Longitude, _predictedImpact.TimeToImpact));
            }
            else
            {
                GUILayout.Label("Impact: none predicted");
            }

            if (_mode == GuidanceMode.GuidedTarget && _hasTarget)
            {
                GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "Target distance: {0:0} m", _targetDistance));
            }

            GUI.DragWindow();
        }

        private double Slider(string label, double value, double min, double max, string format)
        {
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "{0}: {1}", label, value.ToString(format, CultureInfo.InvariantCulture)));
            return GUILayout.HorizontalSlider((float)value, (float)min, (float)max);
        }

        private void Arm()
        {
            if (_vessel == null || !_vessel.IsControllable)
            {
                _lastMessage = "No controllable vessel";
                return;
            }

            _armed = true;
            _gearCommanded = false;
            _phase = FlightPhase.Coast;
            _lastMessage = "Armed";
            SetSasMode(VesselAutopilot.AutopilotMode.Retrograde);
        }

        private void Abort(string message)
        {
            _armed = false;
            _pickTarget = false;
            _commandThrottle = 0f;
            _commandPitch = 0f;
            _commandYaw = 0f;
            _commandRoll = 0f;
            _phase = FlightPhase.Aborted;
            _lastMessage = message;
        }

        private void ApplyLatLonTarget()
        {
            double lat;
            double lon;
            if (!TryParseDouble(_targetLatText, out lat) || !TryParseDouble(_targetLonText, out lon))
            {
                _lastMessage = "Could not parse target lat/lon";
                return;
            }

            SetTarget(lat, lon, "Target set from lat/lon");
        }

        private void CaptureCurrentPosition()
        {
            if (_vessel == null || _vessel.mainBody == null)
            {
                return;
            }

            Vector3d pos = _vessel.GetWorldPos3D();
            SetTarget(_vessel.mainBody.GetLatitude(pos, false), _vessel.mainBody.GetLongitude(pos, false), "Target set to vessel position");
        }

        private void CapturePredictedImpact()
        {
            if (!_predictedImpact.HasImpact)
            {
                _lastMessage = "No impact predicted";
                return;
            }

            SetTarget(_predictedImpact.Latitude, _predictedImpact.Longitude, "Target set to predicted impact");
        }

        private void SetTarget(double lat, double lon, string message)
        {
            _targetLat = Clamp(lat, -90.0, 90.0);
            _targetLon = NormalizeLongitude(lon);
            _targetLatText = _targetLat.ToString("0.000000", CultureInfo.InvariantCulture);
            _targetLonText = _targetLon.ToString("0.000000", CultureInfo.InvariantCulture);
            _hasTarget = true;
            _mode = GuidanceMode.GuidedTarget;
            _lastMessage = message;
        }

        private void HandleTargetPick()
        {
            if (!Input.GetMouseButtonDown(0) || _vessel == null || _vessel.mainBody == null)
            {
                return;
            }

            Camera camera = Camera.main;
            if (MapView.MapIsEnabled && PlanetariumCamera.Camera != null)
            {
                camera = PlanetariumCamera.Camera;
            }
            else if (FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null)
            {
                camera = FlightCamera.fetch.mainCamera;
            }

            if (camera == null)
            {
                _lastMessage = "No camera for target pick";
                return;
            }

            double lat;
            double lon;
            if (TryPickBodyPoint(camera, _vessel.mainBody, out lat, out lon))
            {
                SetTarget(lat, lon, "Target picked under mouse");
                _pickTarget = false;
            }
            else
            {
                _lastMessage = "Mouse ray missed the active body";
            }
        }

        private bool TryPickBodyPoint(Camera camera, CelestialBody body, out double lat, out double lon)
        {
            lat = 0.0;
            lon = 0.0;

            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            Vector3d origin = ToVector3d(ray.origin);
            Vector3d direction = ToVector3d(ray.direction.normalized);
            Vector3d center = body.position;
            Vector3d oc = origin - center;
            double radius = body.Radius;

            double b = 2.0 * Vector3d.Dot(oc, direction);
            double c = Vector3d.Dot(oc, oc) - radius * radius;
            double discriminant = b * b - 4.0 * c;

            if (discriminant < 0.0)
            {
                return false;
            }

            double sqrt = Math.Sqrt(discriminant);
            double t0 = (-b - sqrt) * 0.5;
            double t1 = (-b + sqrt) * 0.5;
            double t = t0 > 0.0 ? t0 : t1;

            if (t <= 0.0)
            {
                return false;
            }

            Vector3d hit = origin + direction * t;
            lat = body.GetLatitude(hit, false);
            lon = body.GetLongitude(hit, false);
            return true;
        }

        private void UpdateGuidance()
        {
            _commandThrottle = 0f;
            _commandPitch = 0f;
            _commandYaw = 0f;
            _commandRoll = 0f;

            Vessel vessel = _vessel;
            if (vessel == null || vessel.mainBody == null)
            {
                _phase = FlightPhase.Idle;
                _lastMessage = "No active vessel";
                return;
            }

            CelestialBody body = vessel.mainBody;
            Vector3d position = vessel.GetWorldPos3D();
            Vector3d up = (position - body.position).normalized;
            Vector3d surfaceVelocity = vessel.srf_velocity;

            _radarAlt = GetRadarAltitude(vessel);
            _verticalSpeed = Vector3d.Dot(surfaceVelocity, up);
            _horizontalSpeed = Math.Max(0.0, Math.Sqrt(Math.Max(0.0, surfaceVelocity.sqrMagnitude - _verticalSpeed * _verticalSpeed)));
            _predictedImpact = PredictBallisticImpact(vessel);

            double mass = Math.Max(0.001, vessel.GetTotalMass());
            double thrust = GetAvailableThrustN(vessel);
            double localGravity = body.gravParameter / Math.Max(1.0, (position - body.position).sqrMagnitude);
            double maxAccel = thrust / mass;
            double netDecel = Math.Max(0.1, maxAccel - localGravity);
            double downwardSpeed = Math.Max(0.0, -_verticalSpeed);
            double stoppingDistance = Math.Max(0.0, (downwardSpeed * downwardSpeed - _touchdownSpeed * _touchdownSpeed) / (2.0 * netDecel));

            _availableTwr = localGravity > 0.0 ? maxAccel / localGravity : 0.0;
            _burnStartAltitude = stoppingDistance * _burnMargin + 5.0;
            _targetDistance = ComputeTargetDistance(body, position);

            if (!_armed)
            {
                _phase = FlightPhase.Idle;
                return;
            }

            if (vessel.LandedOrSplashed)
            {
                _phase = FlightPhase.Landed;
                _lastMessage = "Landed";
                return;
            }

            if (thrust <= 1.0)
            {
                _phase = FlightPhase.Coast;
                _lastMessage = "No usable engine thrust";
                return;
            }

            if (_deployGear && !_gearCommanded && _radarAlt <= _gearDeployAltitude)
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, true);
                _gearCommanded = true;
            }

            Vector3d desiredDirection = ComputeDesiredThrustDirection(vessel, body, position, up, surfaceVelocity);

            bool shouldDeorbit = _mode == GuidanceMode.GuidedTarget
                && _autoDeorbit
                && _hasTarget
                && !_predictedImpact.HasImpact
                && vessel.altitude > Math.Max(10000.0, body.atmosphereDepth + 1000.0);

            if (shouldDeorbit)
            {
                _phase = FlightPhase.Deorbit;
                _commandThrottle = (float)Clamp(_deorbitThrottle, 0.0, 1.0);
                SetSasMode(VesselAutopilot.AutopilotMode.Retrograde);
                _lastMessage = "Retrograde deorbit assist";
                return;
            }

            if (_radarAlt <= _burnStartAltitude || (_radarAlt < 250.0 && _verticalSpeed < -8.0))
            {
                _phase = _radarAlt < 40.0 ? FlightPhase.FinalLanding : FlightPhase.SuicideBurn;
                _commandThrottle = ComputeLandingThrottle(maxAccel, localGravity, downwardSpeed, _radarAlt);
                _lastMessage = "Powered descent";
            }
            else if (_mode == GuidanceMode.GuidedTarget && _hasTarget && _targetDistance > _targetDeadband && _radarAlt < 25000.0)
            {
                _phase = FlightPhase.Correcting;
                double correction = Clamp(_targetDistance / 30000.0, 0.04, 0.25);
                _commandThrottle = (float)Math.Min(correction, _deorbitThrottle);
                _lastMessage = "Target correction";
            }
            else
            {
                _phase = FlightPhase.Coast;
                _commandThrottle = 0f;
                _lastMessage = "Coasting to burn start";
            }

            if (_enableSteering)
            {
                CommandAttitude(vessel, desiredDirection);
            }
            else
            {
                SetSasMode(VesselAutopilot.AutopilotMode.Retrograde);
            }
        }

        private float ComputeLandingThrottle(double maxAccel, double localGravity, double downwardSpeed, double radarAlt)
        {
            if (maxAccel <= localGravity)
            {
                return 1f;
            }

            double remaining = Math.Max(1.0, radarAlt - 2.0);
            double desiredDecel = (downwardSpeed * downwardSpeed - _touchdownSpeed * _touchdownSpeed) / (2.0 * remaining);
            desiredDecel = Math.Max(0.0, desiredDecel);

            double targetDescent = Math.Sqrt(Math.Max(_touchdownSpeed * _touchdownSpeed, 2.0 * Math.Max(0.0, radarAlt - 2.0) * Math.Max(0.1, maxAccel - localGravity) / _burnMargin));
            targetDescent = Clamp(targetDescent, _touchdownSpeed, 35.0);

            double velocityError = downwardSpeed - targetDescent;
            double accelCommand = localGravity + desiredDecel + velocityError * 0.08;

            if (radarAlt < 30.0)
            {
                double lowAltTarget = Clamp(radarAlt * 0.18 + _touchdownSpeed, _touchdownSpeed, 6.0);
                accelCommand = localGravity + (downwardSpeed - lowAltTarget) * 0.45;
            }

            return (float)Clamp(accelCommand / maxAccel, 0.0, 1.0);
        }

        private Vector3d ComputeDesiredThrustDirection(Vessel vessel, CelestialBody body, Vector3d position, Vector3d up, Vector3d surfaceVelocity)
        {
            Vector3d retrograde = surfaceVelocity.sqrMagnitude > 0.25 ? -surfaceVelocity.normalized : up;
            Vector3d desired = retrograde;

            if (_mode == GuidanceMode.GuidedTarget && _hasTarget)
            {
                Vector3d targetWorld = TargetWorldPosition(body);
                Vector3d toTarget = ProjectOnPlane(targetWorld - position, up);

                if (toTarget.sqrMagnitude > 1.0)
                {
                    Vector3d horizontalVelocity = ProjectOnPlane(surfaceVelocity, up);
                    double closingSpeed = Vector3d.Dot(horizontalVelocity, toTarget.normalized);
                    double desiredClosing = Clamp(_targetDistance / 18.0, 0.0, 120.0);
                    double steering = Clamp((desiredClosing - closingSpeed) * 0.006 + _targetDistance / 60000.0, 0.0, _maxTargetSteer);
                    desired = (retrograde + toTarget.normalized * steering).normalized;
                }
            }

            if (_radarAlt < 80.0)
            {
                desired = (desired * 0.65 + up * 0.35).normalized;
            }

            return desired;
        }

        private void CommandAttitude(Vessel vessel, Vector3d desiredWorldDirection)
        {
            if (vessel.ReferenceTransform == null)
            {
                return;
            }

            Vector3 local = vessel.ReferenceTransform.InverseTransformDirection(ToVector3(desiredWorldDirection.normalized));
            float gain = 1.7f;

            _commandYaw = ClampAxis(local.x * gain);
            _commandPitch = ClampAxis(-local.z * gain);
            _commandRoll = 0f;
        }

        private void OnFlyByWire(FlightCtrlState st)
        {
            if (!_armed || _vessel == null)
            {
                return;
            }

            st.mainThrottle = _commandThrottle;

            if (_enableSteering)
            {
                st.pitch = _commandPitch;
                st.yaw = _commandYaw;
                st.roll = _commandRoll;
                st.killRot = false;
            }

            if (_gearCommanded)
            {
                st.gearDown = true;
            }
        }

        private void SetSasMode(VesselAutopilot.AutopilotMode mode)
        {
            if (_vessel == null || _vessel.Autopilot == null)
            {
                return;
            }

            _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            _vessel.Autopilot.Enabled = true;

            if (_vessel.Autopilot.CanSetMode(mode))
            {
                _vessel.Autopilot.SetMode(mode);
            }
        }

        private ImpactSolution PredictBallisticImpact(Vessel vessel)
        {
            ImpactSolution solution = new ImpactSolution();

            if (vessel == null || vessel.mainBody == null)
            {
                return solution;
            }

            CelestialBody body = vessel.mainBody;
            Vector3d pos = vessel.GetWorldPos3D();
            Vector3d vel = vessel.obt_velocity;
            double dt = 1.0;

            for (int i = 0; i < 2400; i++)
            {
                Vector3d toBody = body.position - pos;
                double r2 = Math.Max(1.0, toBody.sqrMagnitude);
                Vector3d accel = toBody.normalized * (body.gravParameter / r2);

                vel += accel * dt;
                pos += vel * dt;

                double alt = body.GetAltitude(pos);
                double lat = body.GetLatitude(pos, false);
                double lon = NormalizeLongitude(body.GetLongitude(pos, false));
                double terrain = Math.Max(0.0, SafeTerrainAltitude(body, lat, lon));

                if (alt <= terrain + 5.0)
                {
                    solution.HasImpact = true;
                    solution.Latitude = lat;
                    solution.Longitude = lon;
                    solution.TimeToImpact = (i + 1) * dt;
                    solution.Altitude = alt;
                    return solution;
                }
            }

            return solution;
        }

        private static double GetAvailableThrustN(Vessel vessel)
        {
            double thrust = 0.0;
            foreach (ModuleEngines engine in vessel.FindPartModulesImplementing<ModuleEngines>())
            {
                if (!engine.EngineIgnited || engine.flameout || !engine.isOperational)
                {
                    continue;
                }

                double engineThrust = engine.maxThrust * Math.Max(0.0, engine.thrustPercentage / 100.0);
                thrust += engineThrust * 1000.0;
            }

            return thrust;
        }

        private double ComputeTargetDistance(CelestialBody body, Vector3d currentPosition)
        {
            if (!_hasTarget || body == null)
            {
                return 0.0;
            }

            double lat = body.GetLatitude(currentPosition, false);
            double lon = body.GetLongitude(currentPosition, false);
            return SurfaceDistance(body, lat, lon, _targetLat, _targetLon);
        }

        private Vector3d TargetWorldPosition(CelestialBody body)
        {
            double terrain = Math.Max(0.0, SafeTerrainAltitude(body, _targetLat, _targetLon));
            return body.GetWorldSurfacePosition(_targetLat, _targetLon, terrain);
        }

        private static double SurfaceDistance(CelestialBody body, double latA, double lonA, double latB, double lonB)
        {
            double phi1 = latA * DegToRad;
            double phi2 = latB * DegToRad;
            double dPhi = (latB - latA) * DegToRad;
            double dLambda = (lonB - lonA) * DegToRad;
            double sinPhi = Math.Sin(dPhi * 0.5);
            double sinLambda = Math.Sin(dLambda * 0.5);
            double a = sinPhi * sinPhi + Math.Cos(phi1) * Math.Cos(phi2) * sinLambda * sinLambda;
            double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0.0, 1.0 - a)));
            return body.Radius * c;
        }

        private static double SafeTerrainAltitude(CelestialBody body, double lat, double lon)
        {
            try
            {
                return body.TerrainAltitude(lat, lon, true);
            }
            catch
            {
                return 0.0;
            }
        }

        private static double GetRadarAltitude(Vessel vessel)
        {
            double radarAlt = vessel.radarAltitude;
            if (double.IsNaN(radarAlt) || double.IsInfinity(radarAlt) || radarAlt <= 0.0)
            {
                radarAlt = vessel.altitude - Math.Max(0.0, vessel.terrainAltitude);
            }

            return Math.Max(0.0, radarAlt);
        }

        private static Vector3d ProjectOnPlane(Vector3d vector, Vector3d normal)
        {
            return vector - normal * Vector3d.Dot(vector, normal);
        }

        private static bool TryParseDouble(string value, out double result)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        }

        private static double NormalizeLongitude(double lon)
        {
            while (lon > 180.0)
            {
                lon -= 360.0;
            }

            while (lon < -180.0)
            {
                lon += 360.0;
            }

            return lon;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private static float ClampAxis(float value)
        {
            if (value < -1f)
            {
                return -1f;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }

        private static Vector3 ToVector3(Vector3d value)
        {
            return new Vector3((float)value.x, (float)value.y, (float)value.z);
        }

        private static Vector3d ToVector3d(Vector3 value)
        {
            return new Vector3d(value.x, value.y, value.z);
        }
    }
}
