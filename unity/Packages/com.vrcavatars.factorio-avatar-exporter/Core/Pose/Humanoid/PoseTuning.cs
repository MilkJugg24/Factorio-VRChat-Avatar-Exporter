using System;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// Adjustments to <see cref="HumanoidAnimations"/> for one avatar, set in the exporter window while the
    /// previews play, so the animations suit its build. The defaults change nothing. Public fields, so Unity keeps
    /// them with the window.
    /// </summary>
    [Serializable]
    public sealed class PoseTuning
    {
        /// <summary>How far the body leans into running, sprinting and flying (1 = as designed).</summary>
        public double Lean = 1;

        /// <summary>Head tilt added to every animation, degrees, positive looking down.</summary>
        public double HeadTilt;

        /// <summary>Arms held further out from the body (positive) or closer in, -1 to 1.</summary>
        public double ArmSpread;

        /// <summary>How far the arms swing while running and sprinting (1 = as designed).</summary>
        public double ArmSwing = 1;

        /// <summary>
        /// How far the elbows bend while running and sprinting (1 = as designed, about square; 0 = straight arms
        /// swinging low at the sides, for a more casual look).
        /// </summary>
        public double ElbowBend = 1;

        /// <summary>
        /// Stride length of the run and the sprint (1 = as designed). The distance covered per frame follows, so
        /// the feet stay planted in the game.
        /// </summary>
        public double Stride = 1;

        /// <summary>How far the wings beat (1 = as designed).</summary>
        public double WingFlap = 1;

        /// <summary>
        /// Wings held higher (positive) or lower, degrees: spread wings point higher, folded ones turn up about
        /// where they join the body.
        /// </summary>
        public double WingRaise;

        /// <summary>Wings turned back (positive) or forward, degrees, in every animation.</summary>
        public double WingSweep;

        /// <summary>How far arms used as wings stroke (1 = as designed).</summary>
        public double ArmWingStroke = 1;

        /// <summary>How far arms used as wings are turned from palms down, degrees (the thumb side up).</summary>
        public double ArmWingRoll = 30;

        /// <summary>
        /// How the avatar flies (<see cref="FlightMethod"/>). Not a slider: the window's Reset leaves it as it is.
        /// </summary>
        public FlightMethod Flight = FlightMethod.WingFlapping;

        /// <summary>How far the tail sways (1 = as designed).</summary>
        public double TailSway = 1;

        /// <summary>Tail held higher (positive) or lower, degrees.</summary>
        public double TailLift;

        /// <summary>A copy with every value inside the range the window offers, so a pose can always be built.</summary>
        public PoseTuning Clamped() => new PoseTuning
        {
            Lean = Clamp(Lean, 0, 2),
            HeadTilt = Clamp(HeadTilt, -30, 30),
            ArmSpread = Clamp(ArmSpread, -1, 1),
            ArmSwing = Clamp(ArmSwing, 0, 2),
            ElbowBend = Clamp(ElbowBend, 0, 1),
            Stride = Clamp(Stride, 0.5, 1.5),
            WingFlap = Clamp(WingFlap, 0, 2),
            WingRaise = Clamp(WingRaise, -45, 45),
            WingSweep = Clamp(WingSweep, -45, 45),
            ArmWingStroke = Clamp(ArmWingStroke, 0, 1.5),
            ArmWingRoll = Clamp(ArmWingRoll, -60, 60),
            TailSway = Clamp(TailSway, 0, 2),
            TailLift = Clamp(TailLift, -45, 45),
            Flight = Flight,
        };

        /// <summary>
        /// The slider values that differ from the defaults, for report.txt; empty when none do. The flight method has a
        /// line of its own (<see cref="FlightMethods.Describe"/>).
        /// </summary>
        public string Describe()
        {
            var defaults = new PoseTuning();
            var parts = new System.Collections.Generic.List<string>();
            void Add(string name, double value, double standard)
            {
                if (Math.Abs(value - standard) > 1e-9)
                {
                    parts.Add(name + " " + value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            Add("lean", Lean, defaults.Lean);
            Add("head tilt", HeadTilt, defaults.HeadTilt);
            Add("arm spread", ArmSpread, defaults.ArmSpread);
            Add("arm swing", ArmSwing, defaults.ArmSwing);
            Add("elbow bend", ElbowBend, defaults.ElbowBend);
            Add("stride", Stride, defaults.Stride);
            Add("wing flap", WingFlap, defaults.WingFlap);
            Add("wing raise", WingRaise, defaults.WingRaise);
            Add("wing sweep", WingSweep, defaults.WingSweep);
            Add("arm-wing stroke", ArmWingStroke, defaults.ArmWingStroke);
            Add("arm-wing roll", ArmWingRoll, defaults.ArmWingRoll);
            Add("tail sway", TailSway, defaults.TailSway);
            Add("tail lift", TailLift, defaults.TailLift);
            return string.Join(", ", parts);
        }

        private static double Clamp(double value, double low, double high) =>
            double.IsNaN(value) ? low : Math.Max(low, Math.Min(high, value));
    }
}
