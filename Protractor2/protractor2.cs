using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Text.RegularExpressions;
using System.Reflection;


namespace Protractor2
{
    public class Protractor2Module : PartModule
    {
        private KSP.IO.PluginConfiguration cfg = KSP.IO.PluginConfiguration.CreateForType<Protractor2Module>();
        Rect mainwindow;
        string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();

        //private static Texture2D protractor2icon = new Texture2D(30, 30, TextureFormat.ARGB32, false);

        private GUIStyle iconstyle = new GUIStyle();

        // Universal time
        double UT = 0;
        string targetBody = "";

        //bool thetatotime = true;

        //private enum orbitbodytype { sun, planet, moon };
        //private Protractor2Module.orbitbodytype orbiting;

        private GameObject approach_obj = new GameObject("Line");
        private double closestApproachTime = -1;
        private CelestialBody sun = Planetarium.fetch.Sun;
        private CelestialBody drawApproachToBody = null;
        private CelestialBody lastknownmainbody = null;
        private LineRenderer approach;
        private PlanetariumCamera cam;

        // Did we initialize, already?
        private bool init = false;

        // Is the config loaded?
        private bool loaded = false;

        // Is the main window visible?
        public static bool isVisible;

        private IButton button;


        #region GUI functions

        public void drawGUI()
        {
            //GUI.skin = HighLogic.Skin;
            GUI.skin = null;
            /*
            if (!ToolbarManager.ToolbarAvailable && HighLogic.LoadedSceneIsFlight && !FlightDriver.Pause)
            {
                if (GUI.Button(new Rect(Screen.width / 6, Screen.height - 34, 32, 32), protractor2icon, iconstyle))
                {
                    if (isVisible == false)
                    {
                        isVisible = true;
                    }
                    else
                    {
                        isVisible = false;
                        approach.enabled = false;
                    }
                }

            }
            */

            if (isVisible)
            {
                mainwindow = GUILayout.Window(897, mainwindow, mainGUI, "Protractor 2 v" + version, GUILayout.Width(200), GUILayout.Height(200));
            } else {
                approach.enabled = false;
            }

        }

        void mainGUI(int windowID)
        {
            CelestialBody target;
            ITargetable t;

            if (!init) {
                initialize();
            }

            if (vessel.mainBody != lastknownmainbody)
            {
                drawApproachToBody = null;
                lastknownmainbody = vessel.mainBody;
                //getorbitbodytype();
            }

            try
            {
                t = FlightGlobals.fetch.VesselTarget;
                if (t != null && t is CelestialBody)
                {
                    target = (CelestialBody) FlightGlobals.fetch.VesselTarget;

                    GUILayout.Label("You have selected " + target.name + " as your target.");

                    if ((vessel.mainBody != sun) && (UT == 0 || UT < Planetarium.GetUniversalTime() || targetBody != target.name))
                    {
                        targetBody = target.name;
                        //Debug.Log("P2: mainBody: " + vessel.mainBody.ToString());
                        //Debug.Log("P2: target: " + target);
                        UT = LambertSolver.NextLaunchWindowUT(vessel.mainBody, target);
                    }

                    // Display time to transfer window
                    if (vessel.mainBody != sun &&
                        (vessel.situation == Vessel.Situations.LANDED ||
                            vessel.situation == Vessel.Situations.ORBITING ||
                            vessel.situation == Vessel.Situations.PRELAUNCH ||
                            vessel.situation == Vessel.Situations.SPLASHED))
                    {
                        GUILayout.Label(TimeToDHMS(UT - Planetarium.GetUniversalTime(), 0) + " to window");
                    }

                    if (vessel.situation == Vessel.Situations.ORBITING ||
                        vessel.situation == Vessel.Situations.FLYING ||
                        vessel.situation == Vessel.Situations.ESCAPING)
                    {
                        GUILayout.Label("Closest approach: " + ToSI(getclosestapproach(target)) + "m");

                        /* TODO: Integrate something like this to give info on minimum dV transfer to target,
                         * if in correct orbits. And also to give info on a node, when we make one. Must
                         * be able to remove data display when node is deleted by user.
                        double tof = LambertSolver.HohmannTimeOfFlight(vessel.mainBody.orbit, target.orbit);
                        double phase_angle = LambertSolver.HohmannPhaseAngle(vessel.mainBody.orbit, target.orbit);
                        double current_phase = LambertSolver.CurrentPhaseAngle(vessel.mainBody.orbit, target.orbit);

                        GUILayout.Label("ToF: " + TimeToDHMS(tof));
                        GUILayout.Label("Transfer θ: " + phase_angle);
                        GUILayout.Label("Current θ: " + current_phase);
                        */

                        // HIGHLY EXPERIMENTAL
                        //if (target.Equals(drawApproachToBody))
                        //{
                        //    drawApproachToBody = null;
                        //}
                        //else
                        //{
                        drawApproachToBody = target;
                        //}
                        drawApproach();
                    }

                    if (vessel.situation == Vessel.Situations.ORBITING)
                    {
                        bool plot = GUILayout.Button("Plot");
                        if (plot)
                        {
                            double UT2;
                            Vector3d dv = LambertSolver.EjectionBurn(vessel, target, out UT2);
                            ManeuverNode mn = vessel.patchedConicSolver.AddManeuverNode(UT2);
                            mn.DeltaV = dv;
                            mn.solver.UpdateFlightPlan();
                            plot = false;

                        }
                    }
                    else
                    {
                        if (vessel.situation == Vessel.Situations.LANDED ||
                            vessel.situation == Vessel.Situations.PRELAUNCH ||
                            vessel.situation == Vessel.Situations.SUB_ORBITAL ||
                            vessel.situation == Vessel.Situations.SPLASHED)
                        {
                            GUILayout.Label("Now get into orbit!");
                        }
                    }
                    
                    if (GUILayout.Button("Remove ALL nodes"))
                    {
                        RemoveAllManeuverNodes(vessel);
                    }

                }
                else
                {
                    GUILayout.Label("No valid target selected");
                }
            }
            catch (ArgumentException ex)
            {
                GUILayout.Label("Could not plot transfer to target: " + ex.Message);
            }
            catch (Exception ex)
            {
                //GUILayout.Label("Error selecting target: " + ex.ToString());
                GUILayout.Label("Error selecting target");
                Debug.Log(ex.ToString ());

            }

            

            GUI.DragWindow();
        }



        public override void OnStart(PartModule.StartState state)
        {
            base.OnStart(state);
            if (state != StartState.Editor)
            {
                loadsettings();
                if ((mainwindow.x == 0) && (mainwindow.y == 0))//mainwindow is used to position the GUI window, lets set it in the center of the screen
                {
                    mainwindow = new Rect(Screen.width / 2, Screen.height / 2, 10, 10);
                }

                approach_obj.layer = 9;

                cam = (PlanetariumCamera)GameObject.FindObjectOfType(typeof(PlanetariumCamera));
                RenderingManager.AddToPostDrawQueue(3, new Callback(drawGUI));

                approach = approach_obj.AddComponent<LineRenderer>();
                approach.transform.parent = null;
                approach.enabled = false;
                approach.SetColors(Color.green, Color.green);
                approach.useWorldSpace = true;
                approach.SetVertexCount(2);
                approach.SetWidth(10, 10);  //was 15, 5

                approach.material = ((MapView)GameObject.FindObjectOfType(typeof(MapView))).orbitLinesMaterial;

                //protractor2icon.LoadImage(KSP.IO.File.ReadAllBytes<Protractor2Module>("protractor-on.png"));

                if (ToolbarManager.ToolbarAvailable)
                {
                    Debug.Log("Protractor2: Blizzy's toolbar present");

                    button = ToolbarManager.Instance.add("Protractor2", "protractor2Button");
                    button.TexturePath = "Protractor2/icon";
                    button.ToolTip = "Toggle Protractor2 UI";
                    button.Visibility = new GameScenesVisibility(GameScenes.FLIGHT);
                    button.OnClick += (e) =>
                    {
                        isVisible = !isVisible;
                    };
                }
                else
                {
                    Debug.Log("Protractor2: Blizzy's toolbar NOT present");
                    //loadicons();
                    isVisible = true;
                }

                RenderingManager.AddToPostDrawQueue(3, new Callback(drawGUI));
            }


        }

        public void OnDestroy()
        {
            if (button != null)
            {
                button.Destroy();
            }
        }

        public override void OnSave(ConfigNode node)
        {
            savesettings();
            base.OnSave(node);
        }


        private void initialize()
        {
            lastknownmainbody = vessel.mainBody;

        //getorbitbodytype();
            init = true;
        }


        public void savesettings()
        {
            if (!loaded) return;
            cfg["config_version"] = version;
            cfg["mainwindow"] = mainwindow;
            cfg["isvisible"] = isVisible;
            /*
            cfg["manualpos"] = manualwindowPos;
            cfg["showadvanced"] = showadvanced;
            cfg["adjustejectangle"] = adjustejectangle;
            cfg["showmanual"] = showmanual;
            cfg["showplanets"] = showplanets;
            cfg["showmoons"] = showmoons;
            cfg["showadvanced"] = showadvanced;
            cfg["showdv"] = showdv;
            cfg["trackdv"] = trackdv;
            */


            Debug.Log("-------------Saved Protractor2 Settings-------------");
            cfg.save();
        }

        public void loadsettings()
        {
            Debug.Log("-------------Loading Protractor2 settings...-------------");

            try
            {
                cfg.load();
                Debug.Log("-------------Protractor2 Settings Opened-------------");
                mainwindow = cfg.GetValue<Rect>("mainwindow");
                isVisible = cfg.GetValue<bool>("isvisible");
                /*
                manualwindowPos = cfg.GetValue<Rect>("manualpos");
                showadvanced = cfg.GetValue<bool>("showadvanced");
                adjustejectangle = cfg.GetValue<bool>("adjustejectangle");
                showmanual = cfg.GetValue<bool>("showmanual");
                showplanets = cfg.GetValue<bool>("showplanets");
                showmoons = cfg.GetValue<bool>("showmoons");
                showdv = cfg.GetValue<bool>("showdv");
                trackdv = cfg.GetValue<bool>("trackdv");
                */
            }
            catch
            {
                Debug.Log("-------------New Protractor2 Settings File Being Created-------------");
                mainwindow = new Rect(0, 0, 0, 0);
                isVisible = true;
                /*
                manualwindowPos = new Rect(0, 0, 0, 0);
                showadvanced = true;
                adjustejectangle = false;
                showmanual = true;

                showplanets = true;
                showmoons = true;
                showdv = true;
                trackdv = true;
                */
                Debug.Log("-------------New Protractor2 Settings File Created-------------");
            }
            loaded = true;  //loaded

            Debug.Log("-------------Loaded Protractor2 Settings-------------");
        }

        #endregion

        public double getclosestapproach(CelestialBody target)
        {
            Orbit closestorbit = new Orbit();
            closestorbit = getclosestorbit(target);
            if (closestorbit.referenceBody == target)
            {
                closestApproachTime = closestorbit.StartUT + closestorbit.timeToPe;
                return closestorbit.PeA;
            }
            else if (closestorbit.referenceBody == target.referenceBody)
            {
                return mindistance(target, closestorbit.StartUT, closestorbit.period / 10, closestorbit) - target.Radius;
            }
            else
            {  
                return mindistance(target, Planetarium.GetUniversalTime(), closestorbit.period / 10, closestorbit) - target.Radius;
            }
        }

        public Orbit getclosestorbit(CelestialBody target)
        {
            Orbit checkorbit = vessel.orbit;
            int orbitcount = 0;

            while (checkorbit.nextPatch != null && checkorbit.patchEndTransition != Orbit.PatchTransitionType.FINAL && orbitcount < 3)  //search for target
            {
                checkorbit = checkorbit.nextPatch;
                orbitcount += 1;
                if (checkorbit.referenceBody == target)
                {
                    return checkorbit;
                }

            }
            checkorbit = vessel.orbit;
            orbitcount = 0;

            while (checkorbit.nextPatch != null && checkorbit.patchEndTransition != Orbit.PatchTransitionType.FINAL && orbitcount < 3) //search for target's referencebody
            {
                checkorbit = checkorbit.nextPatch;
                orbitcount += 1;
                if (checkorbit.referenceBody == target.orbit.referenceBody)
                {
                    return checkorbit;
                }
            }   

            return vessel.orbit;
        }

        public double mindistance(CelestialBody target, double time, double dt, Orbit vesselorbit)
        {
            double[] dist_at_int = new double[11];
            for (int i = 0; i <= 10; i++)
            {
                double step = time + i * dt;
                dist_at_int[i] = (target.getPositionAtUT(step) - vesselorbit.getPositionAtUT(step)).magnitude
                    ;
            }
            double mindist = dist_at_int.Min();
            double maxdist = dist_at_int.Max();
            int minindex = Array.IndexOf(dist_at_int, mindist);

            if (drawApproachToBody == target) closestApproachTime = time + minindex * dt;

            if ((maxdist - mindist) / maxdist >= 0.00001)
                mindist = mindistance(target, time + ((minindex - 1) * dt), dt / 5, vesselorbit);

            return mindist;
        }

        public void drawApproach()
        {
            if (drawApproachToBody != null && MapView.MapIsEnabled && closestApproachTime > 0)
            {
                approach.enabled = true;
                Orbit closeorbit = getclosestorbit(drawApproachToBody);

                if (closeorbit.referenceBody == drawApproachToBody)
                {
                    approach.SetPosition(0, ScaledSpace.LocalToScaledSpace(closeorbit.getTruePositionAtUT(closestApproachTime)));
                }
                else
                {
                    approach.SetPosition(0, ScaledSpace.LocalToScaledSpace(closeorbit.getPositionAtUT(closestApproachTime)));
                }

                approach.SetPosition(1, ScaledSpace.LocalToScaledSpace(drawApproachToBody.orbit.getPositionAtUT(closestApproachTime)));


                float scale = (float)(0.004 * cam.Distance);
                approach.SetWidth(scale, scale);
            }
            else
            {
                approach.enabled = false;
            }
        }


        // CODE FROM MECHJEB FOR TIME STRING - ADD TO CREDITS
        public static int HoursPerDay { get { return GameSettings.KERBIN_TIME ? 6 : 24; } }
        public static int DaysPerYear { get { return GameSettings.KERBIN_TIME ? 426 : 365; } }

        public static string TimeToDHMS(double seconds, int decimalPlaces = 0)
        {
            if (double.IsInfinity(seconds) || double.IsNaN(seconds)) return "Inf";

            string ret = "";
            bool showSecondsDecimals = decimalPlaces > 0;

            try
            {
                string[] units = { "y", "d", "h", "m", "s" };
                long[] intervals = { DaysPerYear * HoursPerDay * 3600, HoursPerDay * 3600, 3600, 60, 1 };

                if (seconds < 0)
                {
                    ret += "-";
                    seconds *= -1;
                }

                for (int i = 0; i < units.Length; i++)
                {
                    long n = (long)(seconds / intervals[i]);
                    bool first = ret.Length < 2;
                    if (!first || (n != 0) || (i == units.Length - 1 && ret == ""))
                    {
                        if (!first) ret += " ";

                        if (showSecondsDecimals && seconds < 60 && i == units.Length -1) ret += seconds.ToString("0." + new string('0', decimalPlaces));
                        else if (first) ret += n.ToString();
                        else ret += n.ToString("00");

                        ret += units[i];
                    }
                    seconds -= n * intervals[i];
                }

            }
            catch (Exception)
            {
                return "NaN";
            }
            return ret;
        }

        //Puts numbers into SI format, e.g. 1234 -> "1.234 k", 0.0045678 -> "4.568 m"
        //maxPrecision is the exponent of the smallest place value that will be shown; for example
        //if maxPrecision = -1 and digitsAfterDecimal = 3 then 12.345 will be formatted as "12.3"
        //while 56789 will be formated as "56.789 k"
        public static string ToSI(double d, int maxPrecision = -99, int sigFigs = 4)
        {
            if (d == 0 || double.IsInfinity(d) || double.IsNaN(d)) return d.ToString() + " ";

            int exponent = (int)Math.Floor(Math.Log10(Math.Abs(d))); //exponent of d if it were expressed in scientific notation

            string[] units = new string[] { "y", "z", "a", "f", "p", "n", "μ", "m", "", "k", "M", "G", "T", "P", "E", "Z", "Y" };
            const int unitIndexOffset = 8; //index of "" in the units array
            int unitIndex = (int)Math.Floor(exponent / 3.0) + unitIndexOffset;
            if (unitIndex < 0) unitIndex = 0;
            if (unitIndex >= units.Length) unitIndex = units.Length - 1;
            string unit = units[unitIndex];

            int actualExponent = (unitIndex - unitIndexOffset) * 3; //exponent of the unit we will us, e.g. 3 for k.
            d /= Math.Pow(10, actualExponent);

            int digitsAfterDecimal = sigFigs - (int)(Math.Ceiling(Math.Log10(Math.Abs(d))));

            if (digitsAfterDecimal > actualExponent - maxPrecision) digitsAfterDecimal = actualExponent - maxPrecision;
            if (digitsAfterDecimal < 0) digitsAfterDecimal = 0;

            string ret = d.ToString("F" + digitsAfterDecimal) + " " + unit;

            return ret;
        }


        public static void RemoveAllManeuverNodes(Vessel vessel)
        {
            while (vessel.patchedConicSolver.maneuverNodes.Count > 0)
            {
                vessel.patchedConicSolver.RemoveManeuverNode(vessel.patchedConicSolver.maneuverNodes.Last());
            }
        }


    /*
        public void getorbitbodytype()
        {
            if (vessel.mainBody == Planetarium.fetch.Sun)
            {
                orbiting = orbitbodytype.sun;
            }
            else if (vessel.mainBody.referenceBody != Planetarium.fetch.Sun)
            {
                orbiting = orbitbodytype.moon;
            }
            else
            {
                orbiting = orbitbodytype.planet;
            }
        }
*/
        public double Angle2d(Vector3d vector1, Vector3d vector2) //projects two vectors to 2D plane and returns angle between them
        {
            Vector3d v1 = Vector3d.Project(new Vector3d(vector1.x, 0, vector1.z), vector1);
            Vector3d v2 = Vector3d.Project(new Vector3d(vector2.x, 0, vector2.z), vector2);
            return Vector3d.Angle(v1, v2);
        }
        /*
        public double CurrentPhase(CelestialBody target)//calculates phase angle between the current body and target body
        {
            Vector3d vecthis = new Vector3d();
            Vector3d vectarget = new Vector3d();
            vectarget = target.orbit.getRelativePositionAtUT(Planetarium.GetUniversalTime());

            if (target.referenceBody == Sun && orbiting == orbitbodytype.moon) //vessel orbits a moon, target is a planet (going down)
            {
                vecthis = vessel.mainBody.referenceBody.orbit.getRelativePositionAtUT(Planetarium.GetUniversalTime());
            }
            else if (vessel.mainBody == target.referenceBody) //vessel and target orbit same body (going parallel)
            {
                vecthis = vessel.orbit.getRelativePositionAtUT(Planetarium.GetUniversalTime()); //going up
            }
            else
            {
                vecthis = vessel.mainBody.orbit.getRelativePositionAtUT(Planetarium.GetUniversalTime());
            }

            double phase = Angle2d(vecthis, vectarget);

            vecthis = Quaternion.AngleAxis(90, Vector3d.forward) * vecthis;

            if (Angle2d(vecthis, vectarget) > 90) phase = 360 - phase;

            return (phase + 360) % 360;
        }
        */

        public double DesiredPhase(CelestialBody dest) //calculates phase angle for rendezvous between two bodies orbiting same parent
        {
            CelestialBody orig = vessel.mainBody;
            double o_alt =
                (vessel.mainBody == dest.orbit.referenceBody) ?
                (vessel.mainBody.GetAltitude(vessel.findWorldCenterOfMass())) + dest.referenceBody.Radius : //going "up" from sun -> planet or planet -> moon
                calcmeanalt(orig); //going lateral from moon -> moon or planet -> planet
            double d_alt = calcmeanalt(dest);
            double u = dest.referenceBody.gravParameter;
            double th = Math.PI * Math.Sqrt(Math.Pow(o_alt + d_alt, 3) / (8 * u));
            double phase = (180 - Math.Sqrt(u / d_alt) * (th / d_alt) * (180 / Math.PI));
            while (phase < 0) phase += 360;
            return phase % 360;
        }

        public double calcmeanalt(CelestialBody body)
        {
            return body.orbit.semiMajorAxis * (1 + body.orbit.eccentricity * body.orbit.eccentricity / 2);
        }
        /* Get ejection angle of node */
        /*
        private void drawEAngle() {
            // Ejection angle
            if(options.showEAngle) {
                String eangle = "n/a";
                if (!FlightGlobals.ActiveVessel.orbit.referenceBody.isSun()) {
                    double angle = FlightGlobals.ActiveVessel.orbit.getEjectionAngle(curState.node);
                    if (!double.IsNaN(angle)) {
                        eangle = Math.Abs(angle).ToString("0.##") + "° from " + ((angle >= 0) ? "prograde" : "retrograde");
                    }
                }
                GUILayout.Label("Ejection angle:", 100, eangle, 150);

                String einclination = "n/a";
                if (!FlightGlobals.ActiveVessel.orbit.referenceBody.isSun()) {
                    double angle = FlightGlobals.ActiveVessel.orbit.getEjectionInclination(curState.node);
                    if (!double.IsNaN(angle)) {
                        einclination = Math.Abs(angle).ToString("0.##") + "° " + ((angle >= 0) ? "north" : "south");
                    }
                }
                GUILayout.Label("Eject. inclination:", 100, einclination, 150);
            }
        }
        */

    }
}
