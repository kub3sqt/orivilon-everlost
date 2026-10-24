namespace Orivilon.Player
{
    /// <summary>
    /// Pevný startovní vzhled – schválená postava „Cestovatel“ v004 (autorské zdroje v Character/, Unity import
    /// v Resources/Characters/TravelerV004). Není součástí seedů světa ani náhodného výběru skinů.
    /// </summary>
    public static class DefaultPlayerSkin
    {
        public const string Id = "everlost:default_traveler_v004";
        public const string DisplayName = "Cestovatel";
        public const string ModelName = "Everlost_Traveler_Default";
        public const string ResourcePath = "Characters/TravelerV004/Everlost_Traveler_Default";
        public const string AxePath = "Characters/TravelerV004/Tool_Axe";
        public const string PickaxePath = "Characters/TravelerV004/Tool_Pickaxe";

        /// <summary>Výška modelu i s kloboukem v jednotkách modelu (metry v Blenderu).</summary>
        public const float ModelHeight = 1.60f;
    }
}
