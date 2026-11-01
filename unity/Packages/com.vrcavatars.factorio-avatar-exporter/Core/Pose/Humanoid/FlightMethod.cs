namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// How an avatar flies in its pack's flight animations (hover, fly, take-off and their gun versions), chosen
    /// in the exporter window (<see cref="PoseTuning.Flight"/>).
    /// </summary>
    public enum FlightMethod
    {
        /// <summary>
        /// The wings beat; the arms hang or hold the gun. The pack also gets the arm-flapping set
        /// (<see cref="AnimationSpec.HoverFlap"/> and the others), so each player can choose it in the game.
        /// </summary>
        WingFlapping,

        /// <summary>The arms beat as wings (for avatars whose wings are their arms); wing bones stay as saved.</summary>
        ArmFlapping,

        /// <summary>The avatar floats: the arms hang or hold the gun, wing bones stay as saved.</summary>
        Floating,
    }

    public static class FlightMethods
    {
        /// <summary>The method as report.txt and the window name it.</summary>
        public static string Describe(this FlightMethod method)
        {
            switch (method)
            {
                case FlightMethod.ArmFlapping: return "arm flapping";
                case FlightMethod.Floating: return "floating";
                default: return "wing flapping (players can also choose arm flapping in the game)";
            }
        }
    }
}
