using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Orivilon.Core;
using Orivilon.Player;
using Orivilon.World.Generation;

#if UNITY_EDITOR
using UnityEditor;
using System.Net;
#endif

namespace Orivilon.Player
{
    /// <summary>
    /// Kompletní ovladač hráče z pohledu první osoby.
    /// Řídí pohyb kamery (yaw/pitch), sprint s FOV efektem, skok, dřep, plavání, létání a head bob.
    /// Fyzika pohybu je realizována přes Rigidbody a AddForce (ne CharacterController).
    /// Vstup je blokován přes SceneLoader.InputBlocked během načítání.
    ///
    /// <para>POHYB JE ŘÍZEN STAVOVÝM AUTOMATEM (<see cref="PlayerMoveState"/>). Platí tři
    /// pravidla, na kterých stojí zbytek třídy:</para>
    /// <list type="number">
    /// <item>Vstup se čte VÝHRADNĚ v <c>Update</c> (<see cref="ReadIntent"/>) a ukládá se
    /// do <see cref="MoveIntent"/>. Ve <c>FixedUpdate</c> už se na klávesy nikdo neptá.</item>
    /// <item>O stavu rozhoduje jediné místo (<see cref="UpdateState"/>) s pevnou prioritou
    /// let → plavání → země → vzduch.</item>
    /// <item>Na Rigidbody sahá jen <c>FixedUpdate</c>. <c>rb.useGravity</c> a
    /// <c>rb.linearDamping</c> se zapisují na jediném místě (<see cref="ApplyGravity"/>).</item>
    /// </list>
    ///
    /// <para>Původní příznaky <c>isGrounded</c>, <c>isFlying</c> a <c>isWalking</c> zůstaly
    /// zachovány jako dopočítávané vlastnosti, aby okolní kód a head bob fungovaly beze změny.
    /// <c>isSprinting</c> je nadále veřejné pole, protože ho čte <c>PlayerNeeds</c> a je
    /// serializované – zapisuje se ale už jen z <see cref="UpdateState"/>.</para>
    /// </summary>
    public class FirstPersonController : MonoBehaviour
    {
        /// <summary>Rigidbody hráče – veškerý pohyb se aplikuje přes AddForce.</summary>
        public Rigidbody rb;

        #region Camera Movement Variables

        /// <summary>Kamera připojená k hráčskému objektu.</summary>
        public Camera playerCamera;

        /// <summary>Výchozí zorný úhel kamery (Field of View) ve stupních.</summary>
        public float fov = 60f;

        /// <summary>Pokud true, pohyb myši ve svislé ose je invertován.</summary>
        public bool invertCamera = false;

        /// <summary>Příznak, zda může kamera rotovat (lze vypnout pro cutscény apod.).</summary>
        public bool cameraCanMove = true;

        /// <summary>Citlivost myši pro otáčení kamery (1 = výchozí, 10 = velmi citlivé).</summary>
        public float mouseSensitivity = 2f;

        /// <summary>Maximální úhel natočení kamery nahoru a dolů ve stupních.</summary>
        public float maxLookAngle = 50f;

        /// <summary>Pokud true, kurzor se zamkne a skryje při startu.</summary>
        public bool lockCursor = true;

        /// <summary>Pokud true, na střed obrazovky se zobrazí crosshair sprite.</summary>
        public bool crosshair = true;

        /// <summary>Sprite použitý jako crosshair.</summary>
        public Sprite crosshairImage;

        /// <summary>Barva crosshairu.</summary>
        public Color crosshairColor = Color.white;

        /// <summary>Aktuální horizontální rotace hráče (yaw) v stupních.</summary>
        private float yaw = 0.0f;

        /// <summary>Aktuální vertikální rotace kamery (pitch) v stupních.</summary>
        private float pitch = 0.0f;

        /// <summary>Image komponenta crosshairu (automaticky nalezena v potomcích).</summary>
        private Image crosshairObject;

        #region Camera Zoom Variables

        /// <summary>Pokud true, je zoom povolen.</summary>
        public bool enableZoom = true;

        /// <summary>Pokud true, zoom se drží stisknutím klávesy (hold). Pokud false, přepíná (toggle).</summary>
        public bool holdToZoom = false;

        /// <summary>Klávesa aktivující zoom.</summary>
        public KeyCode zoomKey = KeyCode.Mouse1;

        /// <summary>
        /// Když hráč drží krumpáč, patří pravé tlačítko přisypávání terénu a nesmí
        /// zároveň zoomovat. Vypnutím se vrátí původní chování.
        /// </summary>
        public bool suppressZoomWithPickaxe = true;

        /// <summary>Zorný úhel kamery při maximálním přiblížení.</summary>
        public float zoomFOV = 30f;

        /// <summary>Rychlost přechodu FOV při zoomu (vyšší = rychlejší).</summary>
        public float zoomStepTime = 5f;

        /// <summary>Příznak, zda je zoom aktuálně aktivní.</summary>
        private bool isZoomed = false;

        #endregion
        #endregion

        #region Movement Variables

        /// <summary>Pokud true, hráč může pohybovat postavou.</summary>
        public bool playerCanMove = true;

        /// <summary>Rychlost chůze v jednotkách za sekundu.</summary>
        public float walkSpeed = 5f;

        /// <summary>Maximální změna rychlosti za jeden fyzikální snímek (omezuje klouzání).</summary>
        public float maxVelocityChange = 10f;

        /// <summary>Násobitel gravitace aplikovaný navíc k Unity gravitaci (vyšší = těžší pád).</summary>
        public float gravityMultiplier = 5f;

        /// <summary>
        /// Maximální výška schodu, přes který hráč přejde bez skoku.
        ///
        /// <para>Výchozí hodnota bývala 10 m. To nebyl práh schodu, ale vypnutá kontrola:
        /// <see cref="StepClimb"/> střílí horní paprsek právě do téhle výšky a ten byl
        /// při 10 m prakticky vždy volný, takže se asistence spouštěla na čemkoli se
        /// sklonem pod 60° – hráč lezl po skalách sám od sebe. Zděděnou hodnotu ze scény
        /// hlídá <see cref="StepHeightSane"/>.</para>
        /// </summary>
        [Header("Step Climb")]
        public float stepHeight = 0.45f;

        /// <summary>
        /// Nad touhle výškou se stepHeight považuje za poškozený a za běhu se srazí zpět.
        /// Scéna si serializovanou hodnotu drží sama, změna výchozí hodnoty v kódu ji
        /// nepřepíše – proto ta pojistka.
        /// </summary>
        private const float StepHeightSane = 1.5f;

        /// <summary>
        /// Skutečně použitá výška schodu po ošetření zděděné hodnoty. Počítá se živě,
        /// aby šlo hodnotu ladit v inspektoru za běhu.
        /// </summary>
        private float EffectiveStepHeight => Mathf.Min(stepHeight, StepHeightSane);

        /// <summary>Síla aplikovaná při přelézání schodu (ForceMode.VelocityChange).</summary>
        public float stepSmooth = 1f;

        /// <summary>
        /// Jde hráč po zemi? Dopočítává se ze stavu a z úmyslu, nedrží se zvlášť.
        /// Head bob i obnova staminy se na to ptají stejně jako dřív.
        /// </summary>
        private bool isWalking => moveState == PlayerMoveState.Grounded && intent.HasPlanarInput;

        #region Sprint

        /// <summary>Pokud true, sprint je povolen.</summary>
        public bool enableSprint = true;

        /// <summary>Pokud true, sprint nikdy nevyprší (ignoruje sprintDuration).</summary>
        public bool unlimitedSprint = false;

        /// <summary>Klávesa pro sprint.</summary>
        public KeyCode sprintKey = KeyCode.LeftControl;

        /// <summary>Rychlost sprintu v jednotkách za sekundu.</summary>
        public float sprintSpeed = 7f;

        /// <summary>Jak dlouho (v sekundách) může hráč sprintovat před vyčerpáním.</summary>
        public float sprintDuration = 5f;

        /// <summary>Kolik sekund staminy ubude za sekundu sprintu.</summary>
        public float staminaDrainPerSecond = 1f;

        /// <summary>Kolik sekund staminy se obnoví za sekundu při stání.</summary>
        public float staminaRegenPerSecond = 1f;

        /// <summary>Násobitel obnovy staminy při chůzi.</summary>
        public float walkingStaminaRegenMultiplier = 0.5f;

        /// <summary>Minimální jídlo a voda potřebné pro obnovu staminy.</summary>
        public float staminaRecoveryNeedsThreshold = 50f;

        /// <summary>Minimální zdraví potřebné pro obnovu staminy.</summary>
        public float staminaRecoveryHealthThreshold = 1f;

        /// <summary>Čas (v sekundách) cooldownu po vyčerpání sprintu.</summary>
        public float sprintCooldown = .5f;

        /// <summary>Zorný úhel kamery při sprintu (větší než fov = efekt rychlosti).</summary>
        public float sprintFOV = 80f;

        /// <summary>Rychlost přechodu FOV při sprintu.</summary>
        public float sprintFOVStepTime = 10f;

        /// <summary>Pokud true, zobrazí se sprint bar.</summary>
        public bool useSprintBar = true;

        /// <summary>Pokud true, sprint bar se skryje když je sprint plný.</summary>
        public bool hideBarWhenFull = true;

        /// <summary>Image pozadí sprint baru.</summary>
        public Image sprintBarBG;

        /// <summary>Image popředí sprint baru (výplně).</summary>
        public Image sprintBar;

        /// <summary>Šířka sprint baru jako procento šířky obrazovky.</summary>
        public float sprintBarWidthPercent = .3f;

        /// <summary>Výška sprint baru jako procento výšky obrazovky.</summary>
        public float sprintBarHeightPercent = .015f;

        /// <summary>CanvasGroup sprint baru pro plynulý fade efekt.</summary>
        private CanvasGroup sprintBarCG;

        /// <summary>Příznak, zda hráč aktuálně sprintuje.</summary>
        public bool isSprinting = false;

        /// <summary>Zbývající čas sprintu v sekundách.</summary>
        private float sprintRemaining;

        /// <summary>Vypočtená šířka sprint baru v pixelech.</summary>
        private float sprintBarWidth;

        /// <summary>Vypočtená výška sprint baru v pixelech.</summary>
        private float sprintBarHeight;

        /// <summary>Příznak, zda je sprint v cooldownu po vyčerpání.</summary>
        private bool isSprintCooldown = false;

        /// <summary>Výchozí hodnota cooldownu (pro reset po obnovení sprintu).</summary>
        private float sprintCooldownReset;

        /// <summary>Reference na potřeby hráče používané pro podmínky obnovy staminy.</summary>
        public PlayerNeeds playerNeeds;

        #endregion

        #region Jump

        /// <summary>Pokud true, skok je povolen.</summary>
        public bool enableJump = true;

        /// <summary>Klávesa pro skok.</summary>
        public KeyCode jumpKey = KeyCode.Space;

        /// <summary>Síla skoku aplikovaná jako Impulse na Rigidbody.</summary>
        public float jumpPower = 5f;

        /// <summary>
        /// Stojí hráč na zemi? Dopočítáno ze stavu, ne z vlastního příznaku – ve vodě
        /// ani při letu se zem nepočítá, i kdyby byla paprskem vidět.
        /// Samotný výsledek raycastu je v <see cref="groundProbe"/>.
        /// </summary>
        private bool isGrounded => moveState == PlayerMoveState.Grounded;

        #endregion

        #region Crouch

        /// <summary>Pokud true, dřep je povolen.</summary>
        public bool enableCrouch = true;

        /// <summary>Pokud true, dřep se drží klávesou. Pokud false, přepíná klávesou.</summary>
        public bool holdToCrouch = true;

        /// <summary>Klávesa pro dřep.</summary>
        public KeyCode crouchKey = KeyCode.LeftControl;

        /// <summary>Y scale hráče ve dřepu (hodnota 1 = normální výška).</summary>
        public float crouchHeight = .75f;

        /// <summary>Násobitel snížení rychlosti ve dřepu (0.5 = poloviční rychlost).</summary>
        public float speedReduction = .5f;

        /// <summary>
        /// Aktuální násobitel rychlosti ze dřepu (1 = stoj).
        ///
        /// <para>Existuje kvůli chybě, která tiše poškozovala projekt: <see cref="Crouch"/>
        /// dřív násobil a dělil přímo <see cref="walkSpeed"/>, tedy SERIALIZOVANÉ pole.
        /// Když hra skončila ve dřepu, zůstala v assetu zapsaná poloviční rychlost
        /// natrvalo – a po dalším dřepu čtvrtinová. Násobitel je běhový, serializovaná
        /// hodnota se nikdy nemění.</para>
        /// </summary>
        private float crouchSpeedMultiplier = 1f;

        /// <summary>Příznak, zda hráč aktuálně dřepí.</summary>
        private bool isCrouched = false;

        /// <summary>Výchozí scale hráče (uloženo při startu pro obnovu po dřepu).</summary>
        private Vector3 originalScale;

        #endregion

        #region Flight

        /// <summary>Pokud true, létání je povoleno (debug/admin funkce).</summary>
        public bool enableFlight = false;

        /// <summary>Klávesa pro přepnutí módu létání.</summary>
        public KeyCode flightKey = KeyCode.F;

        /// <summary>Rychlost pohybu při létání.</summary>
        public float flightSpeed = 10f;

        /// <summary>Létá hráč? Dopočítáno ze stavu.</summary>
        private bool isFlying => moveState == PlayerMoveState.Flying;

        #endregion
        #endregion

        #region Head Bob

        /// <summary>Pokud true, kamera se houpá při chůzi.</summary>
        public bool enableHeadBob = true;

        /// <summary>Transform kloubu kamery, který se pohybuje při head bobu.</summary>
        public Transform joint;

        /// <summary>Rychlost houpání kamery (počet period za sekundu).</summary>
        public float bobSpeed = 10f;

        /// <summary>Amplituda houpání na každé ose (X = boční, Y = svislé, Z = dopředu).</summary>
        public Vector3 bobAmount = new Vector3(.15f, .05f, 0f);

        /// <summary>Výchozí lokální pozice kloubu kamery (pro reset při stání).</summary>
        private Vector3 jointOriginalPos;

        /// <summary>Interní časovač sinusové vlny head bobu.</summary>
        private float timer = 0;

        #endregion

        #region Swimming

        /// <summary>Pokud false, voda se ignoruje a ovladač se chová přesně jako dřív.</summary>
        [Header("Swimming")]
        public bool enableSwimming = true;

        /// <summary>
        /// Kolik metrů pod očima leží hrudník. O vstupu do vody rozhoduje hrudník, ne oči –
        /// jinak by hráč začal plavat teprve ve chvíli, kdy se mu voda dostane nad hlavu,
        /// a do té doby by se brodil po dně.
        /// </summary>
        public float chestBelowEye = 0.7f;

        /// <summary>Výška očí nad pivotem. Použije se jen když není přiřazena kamera.</summary>
        public float fallbackEyeHeight = 1.6f;

        /// <summary>Jak hluboko pod hladinou musí být hrudník, aby plavání začalo.</summary>
        public float swimEnterDepth = 0.2f;

        /// <summary>
        /// O kolik výš nad hladinu se musí hrudník dostat, aby plavání skončilo.
        /// Spolu se <see cref="swimEnterDepth"/> tvoří hysterezi: bez ní by se stav
        /// na vlnící se hladině přepínal každý snímek.
        /// </summary>
        public float swimExitMargin = 0.1f;

        /// <summary>Rychlost plavání v metrech za sekundu.</summary>
        public float swimSpeed = 3f;

        /// <summary>Rychlost plavání se sprintem.</summary>
        public float swimSprintSpeed = 4.4f;

        /// <summary>Rychlost svislého plavání (klávesa skoku nahoru, dřepu dolů).</summary>
        public float swimVerticalSpeed = 2.5f;

        /// <summary>Zrychlení ve vodě v m/s². Nižší hodnota = těžkopádnější rozjezd.</summary>
        public float swimAcceleration = 14f;

        /// <summary>Hydrodynamický odpor (Rigidbody.linearDamping) po dobu plavání.</summary>
        public float swimDrag = 4.5f;

        /// <summary>
        /// Vztlakové zrychlení při plném ponoření hrudníku. Musí být větší než gravitace,
        /// jinak se hráč nikdy nevynoří; rovnovážný ponor vychází z poměru obou hodnot.
        /// </summary>
        public float swimBuoyancy = 21f;

        /// <summary>Hloubka ponoru hrudníku, při které je vztlak plný.</summary>
        public float buoyancySpan = 0.8f;

        /// <summary>
        /// Kolik svislé rychlosti zůstane po dopadu do vody. Bez tlumení by skok z útesu
        /// hráče protáhl až ke dnu, protože odpor vody působí až v dalších snímcích.
        /// </summary>
        public float swimEntryDamping = 0.35f;

        /// <summary>Od jakého sklonu pohledu se i u hladiny plave volně ve všech třech osách.</summary>
        public float swimLookDiveAngle = 30f;

        /// <summary>Svislý přírůstek rychlosti, kterým se hráč vytáhne na břeh.</summary>
        public float shoreClimbAssist = 0.6f;

        /// <summary>Do jaké hloubky hrudníku se ještě zkouší výlez na břeh.</summary>
        private const float ShoreClimbMaxDepth = 0.4f;

        /// <summary>Nad touhle svislou rychlostí se asistence vypne, aby hráče nevystřelila.</summary>
        private const float ShoreClimbMaxRise = 1.6f;

        /// <summary>Dosah vodorovného paprsku hledajícího břeh.</summary>
        private const float ShoreClimbProbe = 0.7f;

        /// <summary>Výška, ve které musí být nad břehem volno.</summary>
        private const float ShoreClimbStep = 1.1f;

        #endregion

        #region Climbing

        /// <summary>Pokud false, stěny se ignorují a lezení se nikdy nespustí.</summary>
        [Header("Climbing")]
        public bool enableClimbing = true;

        /// <summary>Jak daleko před hráče sahá test stěny.</summary>
        public float climbCheckDistance = 0.6f;

        /// <summary>
        /// Poloměr SphereCastu hledajícího stěnu. Tenký paprsek by na členitém
        /// Marching Cubes povrchu propadal mezi výstupky a lezení by se rozpadalo
        /// podle toho, kam přesně se hráč dívá.
        /// </summary>
        public float climbProbeRadius = 0.3f;

        /// <summary>Minimální sklon plochy (°), aby se počítala za stěnu, ne za svah.</summary>
        public float minWallAngle = 70f;

        /// <summary>Maximální sklon plochy (°). Nad 90° jde o převis.</summary>
        public float maxWallAngle = 100f;

        /// <summary>Rychlost pohybu po stěně.</summary>
        public float climbSpeed = 2.2f;

        /// <summary>Zrychlení na stěně v m/s².</summary>
        public float climbAcceleration = 18f;

        /// <summary>
        /// Zrychlení, kterým se hráč tiskne ke stěně. Bez něj by po prvním pohybu
        /// od stěny odplul a SphereCast by ji přestal vidět.
        /// </summary>
        public float climbStickForce = 8f;

        /// <summary>Odpor při lezení. Vysoká hodnota drží hráče na místě, když pustí klávesy.</summary>
        public float climbDrag = 8f;

        /// <summary>Kolik sekund staminy ubude za sekundu lezení.</summary>
        public float climbStaminaDrainPerSecond = 1.5f;

        /// <summary>Kolik staminy musí zbývat, aby šlo lezení vůbec začít.</summary>
        public float climbMinStamina = 0.2f;

        /// <summary>Síla odrazu od stěny při skoku z lezení.</summary>
        public float climbJumpOffPower = 4.5f;

        /// <summary>Jak dlouho po opuštění stěny nejde lezení znovu začít.</summary>
        public float climbCooldown = 0.4f;

        /// <summary>Pod touhle vodorovnou rychlostí se hráč považuje za zaseknutého o stěnu.</summary>
        public float climbStallSpeed = 0.8f;

        /// <summary>Jak dlouho musí zaseknutí trvat, než se ze země začne lézt.</summary>
        public float climbStallTime = 0.2f;

        /// <summary>O kolik výš než oči začíná paprsek hledající horní hranu římsy.</summary>
        public float mantleProbeRise = 0.6f;

        /// <summary>Jak daleko za stěnu se hledá plocha, na kterou se dá vytáhnout.</summary>
        public float mantleForwardOffset = 0.7f;

        /// <summary>Do jakého sklonu (°) se plocha nad hranou ještě považuje za schůdnou.</summary>
        public float mantleMaxGroundAngle = 45f;

        /// <summary>Jak dlouho trvá vytažení na římsu.</summary>
        public float mantleDuration = 0.45f;

        /// <summary>O kolik nad cílovou plochu se hráč nejdřív zvedne, než se posune vpřed.</summary>
        public float mantleApexRise = 0.25f;

        /// <summary>Poloměr kontroly volného místa na římse.</summary>
        public float mantleClearanceRadius = 0.3f;

        /// <summary>
        /// O kolik níž než hrudník se stěna hledá podruhé.
        ///
        /// <para>Tenhle druhý pokus je to, co dělá z lezení použitelnou mechaniku.
        /// Když hráč doleze k horní hraně, sonda v úrovni hrudníku už nad stěnu míří
        /// do prázdna – bez druhého pokusu by stav vypadl do Airborne a hráč by
        /// z vrcholu spadl místo aby se vytáhl. Když stěna zmizí nahoře, ale níž pořád
        /// je, znamená to „jsem u hrany", ne „stěna skončila".</para>
        /// </summary>
        public float climbTopProbeDrop = 0.6f;

        #endregion

        #region Stavový automat

        /// <summary>Vidí sonda před hráčem lezitelnou stěnu?</summary>
        private bool hasWall;

        /// <summary>Stěnu vidí jen spodní sonda – hráč je u horní hrany a může se vytáhnout.</summary>
        private bool atWallTop;

        /// <summary>Normála naposledy nalezené stěny.</summary>
        private Vector3 wallNormal = Vector3.forward;

        /// <summary>Zbývající blokace lezení po odrazu nebo vyčerpání staminy.</summary>
        private float climbCooldownTimer;

        /// <summary>Jak dlouho už hráč tlačí do stěny, aniž by se hýbal.</summary>
        private float wallStallTimer;

        /// <summary>Stisk skoku během lezení, zpracovaný až ve fyzikálním snímku.</summary>
        private bool climbJumpQueued;

        /// <summary>Probíhá právě vytažení na římsu?</summary>
        private bool mantling;

        /// <summary>Pozice pivotu na začátku vytažení.</summary>
        private Vector3 mantleStart;

        /// <summary>Mezibod nad hranou – vrchol dvoufázové dráhy.</summary>
        private Vector3 mantleApex;

        /// <summary>Cílová pozice pivotu na římse.</summary>
        private Vector3 mantleEnd;

        /// <summary>Uplynulý čas vytažení.</summary>
        private float mantleTimer;

        /// <summary>Collider hráče – slouží k dopočtu vzdálenosti pivotu od chodidel.</summary>
        private Collider bodyCollider;

        /// <summary>Aktuální stav pohybu. Mění se výhradně přes <see cref="SetState"/>.</summary>
        private PlayerMoveState moveState = PlayerMoveState.Airborne;

        /// <summary>Poslední přečtený úmysl hráče. Plní se v <c>Update</c>, čte ve <c>FixedUpdate</c>.</summary>
        private MoveIntent intent = MoveIntent.None;

        /// <summary>Syrový výsledek raycastu dolů. Stav si ho vykládá, nespoléhá se na něj přímo.</summary>
        private bool groundProbe;

        /// <summary>Přepnuté létání (klávesa F). Samo o sobě ještě není stav.</summary>
        private bool flightRequested;

        /// <summary>Drží se klávesa stoupání při letu (Space) – let má vlastní ovládání.</summary>
        private bool flyUpHeld;

        /// <summary>Drží se klávesa klesání při letu (LeftShift).</summary>
        private bool flyDownHeld;

        /// <summary>Původní odpor Rigidbody, aby se dal po plavání vrátit přesně zpět.</summary>
        private float baseLinearDamping;

        /// <summary>Je v aktuálním sloupci známá hladina?</summary>
        private bool hasWater;

        /// <summary>Výška hladiny v aktuálním sloupci (platí jen když <see cref="hasWater"/>).</summary>
        private float waterLine;

        /// <summary>Hloubka hrudníku pod hladinou v metrech. Záporná = hrudník je nad vodou.</summary>
        private float chestDepth;

        /// <summary>Hloubka očí pod hladinou v metrech. Kladná = hráč se dívá pod vodu.</summary>
        private float eyeDepth;

        /// <summary>
        /// Zbývající čas, po který se plavání drží i bez odpovědi na dotaz na hladinu.
        ///
        /// <para>Není to kosmetika: <c>WaterLevelAt</c> vrací false i pro sloupec, který
        /// ještě není publikovaný nebo je na vyšším LOD. To znamená „nevím", ne „tady voda
        /// není". Bez téhle tolerance by hráč uprostřed jezera na hranici sloupců vypadl
        /// z plavání do volného pádu.</para>
        /// </summary>
        private float waterUnknownGrace;

        /// <summary>Jak dlouho se plavání drží při neznámé hladině.</summary>
        private const float WaterUnknownGrace = 0.6f;

        #endregion

        #region Veřejný stav pohybu

        /// <summary>Aktuální stav pohybu hráče.</summary>
        public PlayerMoveState MoveState => moveState;

        /// <summary>
        /// Hlásí změnu stavu (starý, nový). Háček pro replikaci, zvuky a animace –
        /// odběratel nemusí stav pollovat v <c>Update</c>.
        /// </summary>
        public event Action<PlayerMoveState, PlayerMoveState> MoveStateChanged;

        /// <summary>Stav zabalený pro síť. Viz <see cref="PlayerMoveState"/> – pořadí hodnot je součástí protokolu.</summary>
        public byte NetworkMoveState => (byte)moveState;

        /// <summary>Plave hráč?</summary>
        public bool IsSwimming => moveState == PlayerMoveState.Swimming;

        /// <summary>Leze hráč po stěně?</summary>
        public bool IsClimbing => moveState == PlayerMoveState.Climbing;

        /// <summary>Probíhá vytažení na římsu? Po tu dobu hráč nereaguje na vstup.</summary>
        public bool IsMantling => mantling;

        /// <summary>
        /// Namáhá se hráč? Sprint i lezení stojí síly, takže obojí zrychluje spotřebu
        /// jídla a vody. Čte <c>PlayerNeeds</c> – dřív se ptal jen na sprint, takže
        /// lezení po skále bylo z hlediska potřeb zadarmo.
        /// </summary>
        public bool IsExerting => isSprinting || moveState == PlayerMoveState.Climbing;

        /// <summary>
        /// Vzdálenost pivotu od chodidel. Počítá se z collideru, protože pivot hráče
        /// není na podlaze a dřep mění scale – pevná konstanta by výlez na římsu
        /// posadila buď do země, nebo do vzduchu.
        /// </summary>
        private float PivotToFeet => bodyCollider != null
            ? transform.position.y - bodyCollider.bounds.min.y
            : 1f;

        /// <summary>Jsou oči pod hladinou? Stejná otázka, jakou si klade BiomeAtmosphere.</summary>
        public bool IsSubmerged => eyeDepth > 0f;

        /// <summary>Hloubka ponoru hrudníku v metrech (0 = hrudník nad vodou).</summary>
        public float SubmersionDepth => Mathf.Max(0f, chestDepth);

        /// <summary>Výška hladiny pod hráčem. Platí jen pokud <see cref="IsSwimming"/>.</summary>
        public float WaterLine => waterLine;

        /// <summary>
        /// Bod očí. Bere se přímo z kamery, ne odhadem z pivotu hráče – pivot se mění
        /// dřepem (mění se scale) a odhad by se rozešel s tím, co vidí hráč.
        /// </summary>
        private Vector3 EyePoint => playerCamera != null
            ? playerCamera.transform.position
            : transform.position + Vector3.up * fallbackEyeHeight;

        /// <summary>Bod hrudníku – o vstupu do plavání rozhoduje on.</summary>
        private Vector3 ChestPoint => EyePoint - Vector3.up * chestBelowEye;

        #endregion

        /// <summary>
        /// Inicializace: získá Rigidbody, nastaví FOV, uloží výchozí scale a pozici kloubu.
        /// Inicializuje sprint zbývající čas pokud sprint není neomezený.
        /// </summary>
        private void Awake()
        {
            rb = GetComponent<Rigidbody>();

            // Výchozí odpor se musí zapamatovat teď: plavání ho přepíše a po výlezu
            // z vody není odkud vzít původní hodnotu.
            if (rb != null) baseLinearDamping = rb.linearDamping;

            // Scény uložené dřív nesou stepHeight = 10. Změna výchozí hodnoty v kódu
            // serializovaná data nepřepíše, takže se nesmyslná výška srazí za běhu.
            if (stepHeight > StepHeightSane)
            {
                Debug.LogWarning(string.Format(
                    "FirstPersonController: stepHeight = {0} m je mimo rozsah, používá se {1} m. " +
                    "Oprav hodnotu v inspektoru hráče (sekce Step Climb) a ulož scénu.",
                    stepHeight, StepHeightSane));
            }

            crosshairObject = GetComponentInChildren<Image>();
            // Collider bývá na kořeni, ale u složeného prefabu klidně o úroveň níž.
            bodyCollider = GetComponent<Collider>();
            if (bodyCollider == null) bodyCollider = GetComponentInChildren<Collider>();

            playerCamera.fieldOfView = fov;
            originalScale = transform.localScale;
            jointOriginalPos = joint.localPosition;

            if (playerNeeds == null)
            {
                playerNeeds = GetComponent<PlayerNeeds>();
            }

            if (!unlimitedSprint)
            {
                sprintRemaining = sprintDuration;
                sprintCooldownReset = sprintCooldown;
            }
        }

        /// <summary>
        /// Nastaví zamčení kurzoru a inicializuje sprint bar podle rozlišení obrazovky.
        /// </summary>
        void Start()
        {
            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
            }

            #region Sprint Bar

            sprintBarCG = GetComponentInChildren<CanvasGroup>();

            if (useSprintBar && sprintBarBG != null && sprintBar != null)
            {
                sprintBarBG.gameObject.SetActive(true);
                sprintBar.gameObject.SetActive(true);

                float screenWidth = Screen.width;
                float screenHeight = Screen.height;

                sprintBarWidth = screenWidth * sprintBarWidthPercent;
                sprintBarHeight = screenHeight * sprintBarHeightPercent;

                sprintBarBG.rectTransform.sizeDelta = new Vector3(sprintBarWidth, sprintBarHeight, 0f);
                sprintBar.rectTransform.sizeDelta = new Vector3(sprintBarWidth - 2, sprintBarHeight - 2, 0f);

                if (hideBarWhenFull)
                {
                    sprintBarCG.alpha = 0;
                }
            }

            #endregion
        }

        float camRotation;

        /// <summary>
        /// Každý snímek zpracovává vstup kamery, zoomu, sprintu, skoku, dřepu, létání a head bobu.
        /// Blokováno přes SceneLoader.InputBlocked (během načítání scény).
        /// Pohyb kamery funguje pouze pokud je kurzor zamčen.
        /// </summary>
        /// <summary>
        /// Natoci pohled na dane uhly. Yaw je azimut ve stupnich (0 = sever), pitch je
        /// sklon (zaporny = dolu), orizly na <see cref="maxLookAngle"/>.
        ///
        /// <para>Existuje kvuli ramovani zaberu: pohled se jinak da menit jen mysi a
        /// prirustkove, takze se na konkretni smer neda spolehlive zamirit. Konzole to
        /// vystavuje jako /pohled a da se tim porovnavat tyz vyhled pred zmenou a po ni.
        /// Zapisuje do TYCHZ poli, ktera pouziva mysi pohled, takze se po prvnim pohybu
        /// mysi nic neskubne zpatky.</para>
        /// </summary>
        public void SetLook(float yawDegrees, float pitchDegrees)
        {
            yaw = yawDegrees;
            pitch = Mathf.Clamp(pitchDegrees, -maxLookAngle, maxLookAngle);

            transform.localEulerAngles = new Vector3(0f, yaw, 0f);
            if (playerCamera != null)
                playerCamera.transform.localEulerAngles = new Vector3(pitch, 0f, 0f);
        }

        /// <summary>Aktualni azimut a sklon pohledu ve stupnich.</summary>
        public Vector2 CurrentLook => new Vector2(yaw, pitch);

        private void Update()
        {
            // Svět se měří a stav se přepočítává I PŘI BLOKOVANÉM VSTUPU. Hráč s otevřenou
            // konzolí nebo inventářem je pořád ve vodě a pořád na něj působí gravitace;
            // zamrzlý stav by znamenal, že se v jezeře chová jako na souši.
            SampleWater();
            CheckGround();
            ProbeWall();

            if (SceneLoader.InputBlocked || GameConsole.IsOpen)
            {
                // Vstup se zahodí, ale hráč visící na stěně dál ubírá staminu a může
                // spadnout. Zamrznout ho jen proto, že si otevřel inventář, by z lezení
                // udělalo bezpečné místo k odpočinku.
                intent = MoveIntent.None;
                TickClimbTimers();
                UpdateState();
                return;
            }

            ReadIntent();
            TickClimbTimers();
            UpdateState();

            #region Camera

            if (Cursor.lockState != CursorLockMode.Locked) return;

            if (cameraCanMove)
            {
                yaw = transform.localEulerAngles.y + Input.GetAxis("Mouse X") * mouseSensitivity;

                if (!invertCamera)
                {
                    pitch -= mouseSensitivity * Input.GetAxis("Mouse Y");
                }
                else
                {
                    pitch += mouseSensitivity * Input.GetAxis("Mouse Y");
                }

                pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);

                transform.localEulerAngles = new Vector3(0, yaw, 0);
                playerCamera.transform.localEulerAngles = new Vector3(pitch, 0, 0);
            }

            #region Camera Zoom

            if (enableZoom)
            {
                // Pravé tlačítko patří při krumpáči stavění terénu, ne zoomu.
                //
                // Nestačí stisk ignorovat: v režimu přepínání (holdToZoom = false) je zoom
                // stav, ne akce. Kdyby si ho hráč zapnul s prázdnou rukou a pak vzal krumpáč,
                // zůstal by viset zapnutý a FOV by se nikdy nevrátilo. Proto se rovnou nuluje.
                bool zoomBlocked = suppressZoomWithPickaxe && PlayerEquipment.HoldingPickaxe;
                if (zoomBlocked) isZoomed = false;

                if (!zoomBlocked && Input.GetKeyDown(zoomKey) && !holdToZoom && !isSprinting)
                {
                    if (!isZoomed)
                    {
                        isZoomed = true;
                    }
                    else
                    {
                        isZoomed = false;
                    }
                }

                if (!zoomBlocked && holdToZoom && !isSprinting)
                {
                    if (Input.GetKeyDown(zoomKey))
                    {
                        isZoomed = true;
                    }
                    else if (Input.GetKeyUp(zoomKey))
                    {
                        isZoomed = false;
                    }
                }

                if (isZoomed)
                {
                    playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, zoomFOV, zoomStepTime * Time.deltaTime);
                }
                else if (!isZoomed && !isSprinting)
                {
                    playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, fov, zoomStepTime * Time.deltaTime);
                }
            }

            #endregion
            #endregion

            #region Sprint

            if (enableSprint)
            {
                if (isSprinting)
                {
                    isZoomed = false;
                    playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, sprintFOV, sprintFOVStepTime * Time.deltaTime);

                    if (!unlimitedSprint)
                    {
                        sprintRemaining -= staminaDrainPerSecond * Time.deltaTime;
                        if (sprintRemaining <= 0)
                        {
                            sprintRemaining = 0;
                            isSprinting = false;
                            isSprintCooldown = true;
                        }
                    }
                }
                else
                {
                    RegenerateStamina();
                }

                if (isSprintCooldown)
                {
                    sprintCooldown -= 1 * Time.deltaTime;
                    if (sprintCooldown <= 0)
                    {
                        isSprintCooldown = false;
                    }
                }
                else
                {
                    sprintCooldown = sprintCooldownReset;
                }

                if (useSprintBar && !unlimitedSprint && sprintBar != null)
                {
                    float sprintRemainingPercent = sprintRemaining / sprintDuration;
                    sprintBar.transform.localScale = new Vector3(sprintRemainingPercent, 1f, 1f);
                }
            }

            #endregion

            #region Jump

            if (enableJump && Input.GetKeyDown(jumpKey) && isGrounded)
            {
                Jump();
            }

            #endregion

            #region Crouch

            // Ve vodě klávesa dřepu potápí (viz MoveIntent.Down), takže by zároveň
            // nesměla přepínat dřep – hráč by se pod hladinou scvrkával.
            if (enableCrouch && !IsSwimming)
            {
                if (Input.GetKeyDown(crouchKey) && !holdToCrouch)
                {
                    Crouch();
                }

                if (Input.GetKeyDown(crouchKey) && holdToCrouch)
                {
                    isCrouched = false;
                    Crouch();
                }
                else if (Input.GetKeyUp(crouchKey) && holdToCrouch)
                {
                    isCrouched = true;
                    Crouch();
                }
            }

            #endregion

            // Létání se tu už jen přepíná; rychlost i gravitaci řeší FlightMotor
            // ve FixedUpdate. Fyzika zapsaná z Update byla navíc vázaná na snímkovou
            // frekvenci, takže se let choval jinak při 60 a při 160 FPS.

            if (enableHeadBob)
            {
                HeadBob();
            }
        }

        /// <summary>
        /// Fyzikální snímek. JEDINÉ místo v celé třídě, které sahá na Rigidbody.
        ///
        /// <para>Pořadí je závazné: <see cref="ApplyGravity"/> nejdřív nastaví režim
        /// (gravitace, odpor, vztlak) podle stavu, teprve pak motor daného stavu přidá
        /// pohybové síly. Dřív se <c>rb.useGravity</c> psalo ze dvou smyček naráz a o
        /// výsledku rozhodovalo pořadí volání.</para>
        /// </summary>
        void FixedUpdate()
        {
            // Kinematické tělo znamená, že hráče právě drží někdo jiný – při spawnu
            // ho tak přišpendlí GameManager, dokud pod ním nevznikne collider.
            if (rb == null || rb.isKinematic) return;

            // Teleport drží hráče na místě a k tomu si SÁM vypíná gravitaci
            // (VoxelTerrain.TeleportRoutine si původní hodnotu zapamatuje a na konci ji
            // vrátí). ApplyGravity by mu ji každý fyzikální snímek zase zapnul a hráč by
            // se mezi doskočením a obnovením propadl. Po dobu teleportu se tedy nesahá
            // na nic – ani na gravitaci, ani na pohyb.
            VoxelTerrain terrain = VoxelTerrain.instance;
            if (terrain != null && terrain.IsTeleporting) return;

            ApplyGravity();

            // Vytažení na římsu je řízený pohyb, ne reakce na vstup – dokud běží,
            // dokončí se i s otevřeným inventářem. Přerušit ho v půlce by hráče
            // nechalo viset v geometrii římsy.
            if (mantling)
            {
                MantleMotor();
                return;
            }

            if (SceneLoader.InputBlocked || GameConsole.IsOpen)
                return;

            switch (moveState)
            {
                case PlayerMoveState.Swimming:
                    SwimMotor();
                    break;

                case PlayerMoveState.Climbing:
                    ClimbMotor();
                    break;

                case PlayerMoveState.Flying:
                    FlightMotor();
                    break;

                default:
                    GroundAirMotor();
                    break;
            }
        }

        #region Vstup a stav

        /// <summary>
        /// Přeloží klávesy na úmysl. Jediné místo, kde se čte <c>Input</c> pro pohyb –
        /// motory ve <c>FixedUpdate</c> už pracují jen s <see cref="MoveIntent"/>.
        /// </summary>
        private void ReadIntent()
        {
            Vector2 move = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));

            intent = new MoveIntent(
                move,
                Input.GetKey(jumpKey),
                Input.GetKey(crouchKey),
                Input.GetKey(sprintKey));

            // Let má vlastní klávesy (Space/LeftShift) a ty se nesmí míchat s plaváním:
            // klávesa dřepu je ve výchozím nastavení stejná jako sprint.
            flyUpHeld = Input.GetKey(KeyCode.Space);
            flyDownHeld = Input.GetKey(KeyCode.LeftShift);

            // Při lezení je W/S přiřazené pohybu po stěně, takže klávesa skoku zbývá
            // na odraz. Stisk se jen zaznamená – síla se přidá až ve fyzikálním snímku.
            if (moveState == PlayerMoveState.Climbing && !mantling && Input.GetKeyDown(jumpKey))
                climbJumpQueued = true;

            if (enableFlight && Cursor.lockState == CursorLockMode.Locked && Input.GetKeyDown(flightKey))
                flightRequested = !flightRequested;

            if (!enableFlight) flightRequested = false;
        }

        /// <summary>
        /// Změří hladinu v místě hráče. Ptá se stejného zdroje jako podvodní post-process
        /// (<c>BiomeAtmosphere</c>), takže se obraz a fyzika nemohou rozejít.
        /// </summary>
        private void SampleWater()
        {
            if (!enableSwimming)
            {
                hasWater = false;
                chestDepth = 0f;
                eyeDepth = 0f;
                waterUnknownGrace = 0f;
                return;
            }

            VoxelTerrain terrain = VoxelTerrain.instance;
            Vector3 eye = EyePoint;

            if (terrain != null && terrain.WaterLevelAt(eye.x, eye.z, out float level))
            {
                hasWater = true;
                waterLine = level;
                eyeDepth = level - eye.y;
                chestDepth = level - ChestPoint.y;
                waterUnknownGrace = WaterUnknownGrace;
                return;
            }

            // Odpověď „nevím" (nepublikovaný sloupec nebo vyšší LOD) se nesmí vyložit
            // jako „voda tu není"; viz waterUnknownGrace.
            hasWater = false;
            chestDepth = 0f;
            eyeDepth = 0f;
            waterUnknownGrace = Mathf.Max(0f, waterUnknownGrace - Time.deltaTime);
        }

        /// <summary>
        /// Rozhodne o stavu. Priorita je pevná: let přebíjí všechno (je to debug režim),
        /// voda přebíjí zem, zem přebíjí vzduch.
        /// </summary>
        private void UpdateState()
        {
            PlayerMoveState next;

            if (flightRequested) next = PlayerMoveState.Flying;
            else if (WantsSwim()) next = PlayerMoveState.Swimming;
            else if (WantsClimb()) next = PlayerMoveState.Climbing;
            else if (groundProbe) next = PlayerMoveState.Grounded;
            else next = PlayerMoveState.Airborne;

            if (next != moveState) SetState(next);

            // Jediné místo, kde se sprint ROZHODUJE. Pole zůstává veřejné a serializované,
            // protože ho čte PlayerNeeds. Druhý (a poslední) zápis je vyčerpání staminy
            // v sekci Sprint níž, které sprint jen ukončí.
            isSprinting = enableSprint
                && intent.Sprint
                && intent.HasPlanarInput
                && sprintRemaining > 0f
                && !isSprintCooldown
                && moveState != PlayerMoveState.Flying
                && moveState != PlayerMoveState.Climbing;
        }

        /// <summary>
        /// Má hráč plavat? Dva různé prahy pro vstup a výstup – jediný práh by na vlnící se
        /// hladině přepínal stav každý snímek a hráč by střídavě padal a plaval.
        /// </summary>
        private bool WantsSwim()
        {
            if (!enableSwimming) return false;

            if (!hasWater)
                return moveState == PlayerMoveState.Swimming && waterUnknownGrace > 0f;

            return moveState == PlayerMoveState.Swimming
                ? chestDepth > -swimExitMargin
                : chestDepth > swimEnterDepth;
        }

        /// <summary>
        /// Hledá lezitelnou stěnu před hráčem. SphereCast, ne paprsek: povrch z Marching
        /// Cubes je členitý a tenký paprsek by mezi výstupky propadal, takže by lezení
        /// vypadávalo podle toho, o kolik stupňů hráč pootočil hlavou.
        /// </summary>
        private void ProbeWall()
        {
            hasWall = false;
            atWallTop = false;

            if (!enableClimbing || rb == null) return;

            Vector3 dir = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();

            if (TryCastWall(ChestPoint, dir, out Vector3 normal))
            {
                hasWall = true;
                wallNormal = normal;
                return;
            }

            // Druhý pokus níž: když stěna chybí u hrudníku, ale pod ním je, hráč doleze
            // k horní hraně. Viz climbTopProbeDrop – bez tohohle by z vrcholu spadl.
            if (!TryCastWall(ChestPoint - Vector3.up * climbTopProbeDrop, dir, out normal))
                return;

            hasWall = true;
            atWallTop = true;
            wallNormal = normal;
        }

        /// <summary>Jeden pokus o nalezení lezitelné stěny z daného bodu.</summary>
        private bool TryCastWall(Vector3 origin, Vector3 dir, out Vector3 normal)
        {
            normal = Vector3.zero;

            if (!Physics.SphereCast(origin, climbProbeRadius, dir, out RaycastHit hit,
                                    climbCheckDistance, ~0, QueryTriggerInteraction.Ignore))
                return false;

            // Sklon se měří proti svislici: svah, po kterém se dá vyjít, se lézt nemá.
            float angle = Vector3.Angle(hit.normal, Vector3.up);
            if (angle < minWallAngle || angle > maxWallAngle) return false;

            normal = hit.normal;
            return true;
        }

        /// <summary>
        /// Časovače lezení: blokace po odrazu, měření zaseknutí o stěnu a spotřeba staminy.
        /// Když stamina dojde, hráč padá – to je jediný důvod, proč se lezení ukončuje samo.
        /// </summary>
        private void TickClimbTimers()
        {
            if (climbCooldownTimer > 0f)
                climbCooldownTimer -= Time.deltaTime;

            // Zaseknutí o stěnu: hráč drží dopředu, ale nikam nejede. Je to podmínka pro
            // vstup do lezení ZE ZEMĚ – bez ní by lezení převzalo každou chůzi do kopce.
            Vector3 planarVelocity = Vector3.ProjectOnPlane(rb != null ? rb.linearVelocity : Vector3.zero, Vector3.up);

            if (hasWall && intent.Move.y > 0.1f && planarVelocity.magnitude < climbStallSpeed)
                wallStallTimer += Time.deltaTime;
            else
                wallStallTimer = 0f;

            if (moveState != PlayerMoveState.Climbing || mantling) return;

            if (unlimitedSprint) return;

            sprintRemaining -= climbStaminaDrainPerSecond * Time.deltaTime;

            if (sprintRemaining > 0f) return;

            sprintRemaining = 0f;
            isSprintCooldown = true;
            climbCooldownTimer = climbCooldown;
            SetState(PlayerMoveState.Airborne);
        }

        /// <summary>
        /// Má hráč lézt? Na stěně se visí i bez vstupu – ven se jde jen vyčerpáním
        /// staminy, odrazem, ztrátou stěny nebo vytažením na římsu.
        /// </summary>
        private bool WantsClimb()
        {
            if (!enableClimbing) return false;

            // Rozdělané vytažení nesmí přerušit nic; hráč je uprostřed geometrie římsy.
            if (mantling) return true;

            if (moveState == PlayerMoveState.Climbing) return hasWall;

            if (climbCooldownTimer > 0f) return false;
            if (!hasWall) return false;
            if (moveState == PlayerMoveState.Swimming) return false;
            if (!unlimitedSprint && sprintRemaining <= climbMinStamina) return false;

            // Lezení se nikdy nespustí samo – hráč musí tlačit do stěny.
            if (intent.Move.y <= 0.1f) return false;

            // Ze země navíc teprve po chvíli marného tlačení, aby se chůze podél skály
            // neměnila v lezení při každém otření o stěnu.
            if (groundProbe && wallStallTimer < climbStallTime) return false;

            return true;
        }

        /// <summary>Přechod mezi stavy včetně jednorázových efektů na hranici.</summary>
        private void SetState(PlayerMoveState next)
        {
            PlayerMoveState previous = moveState;
            moveState = next;

            if ((previous == PlayerMoveState.Swimming || previous == PlayerMoveState.Climbing) && rb != null)
            {
                // Zvýšený odpor musí zmizet hned při opuštění stavu, jinak hráč ještě
                // chvíli „brzdí ve vzduchu".
                rb.linearDamping = baseLinearDamping;
            }

            if (previous == PlayerMoveState.Climbing)
            {
                // Rozdělané vytažení se ruší spolu se stavem – jinak by MantleMotor
                // dál táhl hráče k římse, na které už nevisí.
                mantling = false;
                climbJumpQueued = false;
                wallStallTimer = 0f;
            }

            if (next == PlayerMoveState.Climbing && rb != null)
            {
                // Náraz do stěny se nepřenáší do lezení: zbytková rychlost by hráče
                // po přilnutí odmrštila stranou.
                rb.linearVelocity = Vector3.zero;
                if (isCrouched) Crouch();
            }

            if (next == PlayerMoveState.Swimming && rb != null)
            {
                Vector3 v = rb.linearVelocity;
                v.y *= swimEntryDamping;
                rb.linearVelocity = v;

                if (isCrouched) Crouch();
                isZoomed = false;
            }

            if (next == PlayerMoveState.Flying && rb != null)
                rb.linearVelocity = Vector3.zero;

            MoveStateChanged?.Invoke(previous, next);
        }

        #endregion

        #region Motory

        /// <summary>
        /// Nastaví gravitační režim a odpor podle stavu. Jediné místo, kde se zapisuje
        /// <c>rb.useGravity</c> a <c>rb.linearDamping</c>.
        /// </summary>
        private void ApplyGravity()
        {
            switch (moveState)
            {
                case PlayerMoveState.Swimming:
                {
                    rb.useGravity = false;
                    rb.linearDamping = swimDrag;

                    // Vztlak roste s ponorem hrudníku, zbytková gravitace s vynořením.
                    // V rovnováze obou sil se hráč ustálí tak, že má hlavu nad hladinou –
                    // proto se plavání u hladiny nemusí nijak zvlášť „držet nahoře".
                    float submersion = Mathf.Clamp01(chestDepth / Mathf.Max(0.01f, buoyancySpan));

                    rb.AddForce(Physics.gravity * (1f - submersion), ForceMode.Acceleration);
                    rb.AddForce(Vector3.up * (swimBuoyancy * submersion), ForceMode.Acceleration);
                    break;
                }

                case PlayerMoveState.Climbing:
                    // Na stěně gravitace nepůsobí vůbec; hráče drží přítlak z ClimbMotor
                    // a vysoký odpor, aby po puštění kláves nesjížděl dolů.
                    rb.useGravity = false;
                    rb.linearDamping = climbDrag;
                    break;

                case PlayerMoveState.Flying:
                    rb.useGravity = false;
                    rb.linearDamping = baseLinearDamping;
                    break;

                case PlayerMoveState.Grounded:
                    rb.useGravity = true;
                    rb.linearDamping = baseLinearDamping;
                    break;

                default:
                    // Airborne: k Unity gravitaci se přidává násobek, aby byl pád
                    // svižnější. Zachováno z původního chování.
                    rb.useGravity = true;
                    rb.linearDamping = baseLinearDamping;
                    rb.AddForce(Physics.gravity * gravityMultiplier, ForceMode.Acceleration);
                    break;
            }
        }

        /// <summary>Pohyb po zemi a ve vzduchu. Obsahově shodný s původním FixedUpdate.</summary>
        private void GroundAirMotor()
        {
            bool grounded = moveState == PlayerMoveState.Grounded;

            if (grounded && rb.linearVelocity.magnitude > 0.1f)
                StepClimb();

            // Přilepení k zemi: bez něj hráč po nerovnostech terénu nadskakuje.
            if (grounded && rb.linearVelocity.y <= 0f)
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, -2f, rb.linearVelocity.z);

            if (!playerCanMove) return;

            bool sprinting = enableSprint && intent.Sprint && sprintRemaining > 0f && !isSprintCooldown;
            float speed = GetEffectiveSpeed(sprinting);

            Vector3 targetVelocity =
                transform.TransformDirection(new Vector3(intent.Move.x, 0f, intent.Move.y)) * speed;

            Vector3 velocityChange = targetVelocity - rb.linearVelocity;
            velocityChange.x = Mathf.Clamp(velocityChange.x, -maxVelocityChange, maxVelocityChange);
            velocityChange.z = Mathf.Clamp(velocityChange.z, -maxVelocityChange, maxVelocityChange);
            velocityChange.y = 0f;

            if (sprinting && intent.HasPlanarInput)
            {
                // Sprint ve dřepu nedává smysl – hráč se nejdřív postaví.
                if (isCrouched) Crouch();

                if (hideBarWhenFull && !unlimitedSprint && sprintBarCG != null)
                    sprintBarCG.alpha += 5 * Time.deltaTime;
            }

            rb.AddForce(velocityChange, ForceMode.VelocityChange);
        }

        /// <summary>
        /// Plavání. Pod vodou se plave volně ve třech osách podle pohledu, u hladiny
        /// po rovině – jinak by každý pohled dolů hráče při plavání potápěl.
        /// </summary>
        private void SwimMotor()
        {
            Transform cam = playerCamera != null ? playerCamera.transform : transform;

            bool freeDive = eyeDepth > 0f || Mathf.Abs(pitch) > swimLookDiveAngle;

            Vector3 forward = freeDive ? cam.forward : Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            // Doprava se bere vždy vodorovně: šikmé „right" z kamery by při pohledu dolů
            // stáčelo úkroky do svislé roviny a plavání by se rozjíždělo do strany.
            Vector3 right = Vector3.ProjectOnPlane(cam.right, Vector3.up);

            if (forward.sqrMagnitude > 0.0001f) forward.Normalize();
            if (right.sqrMagnitude > 0.0001f) right.Normalize();

            Vector3 wish = forward * intent.Move.y + right * intent.Move.x;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            bool sprinting = enableSprint && intent.Sprint && sprintRemaining > 0f && !isSprintCooldown;

            Vector3 target = wish * (sprinting ? swimSprintSpeed : swimSpeed);

            float vertical = intent.Vertical;
            target += Vector3.up * (vertical * swimVerticalSpeed);

            Vector3 change = target - rb.linearVelocity;

            // Bez svislého úmyslu u hladiny se osa Y přenechává vztlaku. Kdyby ji řídil
            // i motor, srovnal by ji na nulu a hráč by uvízl napůl ponořený.
            if (vertical == 0f && !freeDive)
                change.y = 0f;

            change = Vector3.ClampMagnitude(change, swimAcceleration * Time.fixedDeltaTime);
            rb.AddForce(change, ForceMode.VelocityChange);

            if (intent.Move.y > 0f)
                TryShoreClimb();
        }

        /// <summary>
        /// Výlez z vody na břeh. Bez něj se hráč o svislý břeh zasekne: vztlak ho drží
        /// v hladině a vodorovná síla ho jen tlačí do stěny.
        /// </summary>
        private void TryShoreClimb()
        {
            if (chestDepth > ShoreClimbMaxDepth) return;
            if (rb.linearVelocity.y > ShoreClimbMaxRise) return;

            Vector3 origin = ChestPoint;
            Vector3 dir = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();

            if (!Physics.Raycast(origin, dir, ShoreClimbProbe, ~0, QueryTriggerInteraction.Ignore))
                return;

            // Nad překážkou musí být volno, jinak by se hráč pokoušel vylézt na stěnu.
            if (Physics.Raycast(origin + Vector3.up * ShoreClimbStep, dir, ShoreClimbProbe, ~0, QueryTriggerInteraction.Ignore))
                return;

            rb.AddForce(Vector3.up * shoreClimbAssist, ForceMode.VelocityChange);
        }

        /// <summary>
        /// Pohyb po stěně. Hráč se pohybuje v rovině stěny: svisle podle osy dopředu/dozadu,
        /// vodorovně podle úkroků. Pohled tu směr NEŘÍDÍ – na skále se hráč potřebuje
        /// rozhlížet, aniž by ho to odlepilo od cesty, kterou leze.
        /// </summary>
        private void ClimbMotor()
        {
            if (climbJumpQueued)
            {
                climbJumpQueued = false;
                climbCooldownTimer = climbCooldown;

                // Odraz jde od stěny a zároveň nahoru, aby se dal řetězit přeskok
                // mezi dvěma stěnami komína.
                rb.AddForce(wallNormal * climbJumpOffPower + Vector3.up * (jumpPower * 0.5f),
                            ForceMode.Impulse);

                SetState(PlayerMoveState.Airborne);
                return;
            }

            // Vytažení se zkouší při pohybu vzhůru – a taky když hráč dosáhl horní hrany
            // (atWallTop), i bez vstupu. Traverz pod hranou to nespustí, protože tam
            // sonda v úrovni hrudníku stěnu pořád vidí.
            bool wantsMantle = intent.Move.y > 0.1f || (atWallTop && intent.Move.y >= 0f);
            if (wantsMantle && TryStartMantle()) return;

            Vector3 n = wallNormal;

            // Pořadí operandů není libovolné: Cross(n, up) dává tečnu doprava vzhledem
            // ke směru pohledu do stěny, obrácené pořadí by úkroky zrcadlilo.
            Vector3 tangentRight = Vector3.Cross(n, Vector3.up);
            if (tangentRight.sqrMagnitude < 0.0001f) return;
            tangentRight.Normalize();

            // Svislice promítnutá do roviny stěny – u převisu (> 90°) se tím lezení
            // nakloní dozadu místo aby mířilo mimo stěnu.
            Vector3 tangentUp = Vector3.ProjectOnPlane(Vector3.up, n);
            if (tangentUp.sqrMagnitude < 0.0001f) return;
            tangentUp.Normalize();

            Vector3 wish = tangentUp * intent.Move.y + tangentRight * intent.Move.x;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            Vector3 target = wish * climbSpeed;

            // Řídí se jen tečná složka rychlosti; kolmou nechává na přítlaku níž.
            Vector3 tangentialVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, n);
            Vector3 change = Vector3.ClampMagnitude(target - tangentialVelocity,
                                                    climbAcceleration * Time.fixedDeltaTime);

            rb.AddForce(change, ForceMode.VelocityChange);
            rb.AddForce(-n * climbStickForce, ForceMode.Acceleration);
        }

        /// <summary>
        /// Najde horní hranu římsy a zahájí vytažení. Vrací true, pokud vytažení začalo.
        /// </summary>
        private bool TryStartMantle()
        {
            Vector3 into = Vector3.ProjectOnPlane(-wallNormal, Vector3.up);
            if (into.sqrMagnitude < 0.0001f) return false;
            into.Normalize();

            Vector3 eye = EyePoint;
            Vector3 origin = eye + Vector3.up * mantleProbeRise + into * mantleForwardOffset;

            // Paprsek dolů ZA stěnou: hledá se plocha, na které hráč skončí. Kdyby se
            // hrana hledala dopředu, našla by se i uprostřed stěny každá prasklina.
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                 mantleProbeRise + 1.5f, ~0, QueryTriggerInteraction.Ignore))
                return false;

            if (Vector3.Angle(hit.normal, Vector3.up) > mantleMaxGroundAngle) return false;

            // Hrana pod očima není římsa, ale schod – ten patří StepClimb.
            if (hit.point.y < eye.y) return false;

            Vector3 target = new Vector3(hit.point.x, hit.point.y + PivotToFeet, hit.point.z);

            // Na římse musí být místo. Bez téhle kontroly se hráč vytáhne do skály,
            // pokud je nad hranou převis nebo úzká police.
            if (Physics.CheckSphere(target, mantleClearanceRadius, ~0, QueryTriggerInteraction.Ignore))
                return false;

            mantleStart = transform.position;
            mantleEnd = target;
            mantleApex = new Vector3(mantleStart.x, target.y + mantleApexRise, mantleStart.z);
            mantleTimer = 0f;
            mantling = true;

            rb.linearVelocity = Vector3.zero;
            return true;
        }

        /// <summary>
        /// Vytažení na římsu ve dvou fázích: nejdřív svisle nad hranu, pak vodorovně na ni.
        /// Přímá interpolace by hráče protáhla rohem římsy a fyzika by ho vystrčila zpátky.
        /// </summary>
        private void MantleMotor()
        {
            mantleTimer += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(mantleTimer / Mathf.Max(0.05f, mantleDuration));

            Vector3 position = t < 0.5f
                ? Vector3.Lerp(mantleStart, mantleApex, Mathf.SmoothStep(0f, 1f, t * 2f))
                : Vector3.Lerp(mantleApex, mantleEnd, Mathf.SmoothStep(0f, 1f, (t - 0.5f) * 2f));

            rb.linearVelocity = Vector3.zero;
            rb.MovePosition(position);

            if (t < 1f) return;

            mantling = false;

            // Krátká blokace po doskočení na římsu: hráč obvykle pořád drží dopředu
            // a bez ní by se okamžitě chytil další stěny přímo před sebou.
            climbCooldownTimer = climbCooldown;

            // Zem se prohlásí rovnou: CheckGround by ji ve stejném snímku ještě nemusel
            // vidět a hráč by na vrcholu římsy na okamžik spadl do Airborne.
            groundProbe = true;
            SetState(PlayerMoveState.Grounded);
        }

        /// <summary>Létání. Chování zachováno z původního kódu, jen přesunuto do fyzikálního snímku.</summary>
        private void FlightMotor()
        {
            float flySpeed = flightSpeed;
            if (intent.Sprint) flySpeed *= 2f;

            Vector3 planar =
                transform.TransformDirection(new Vector3(intent.Move.x, 0f, intent.Move.y)) * flightSpeed;

            float verticalVelocity = 0f;
            if (flyDownHeld) verticalVelocity = -flySpeed;
            else if (flyUpHeld) verticalVelocity = flySpeed;

            rb.linearVelocity = new Vector3(planar.x, verticalVelocity, planar.z);
        }

        #endregion

        /// <summary>
        /// Detekuje přítomnost země pod hráčem raycastem dolů.
        /// Hráč je považován za přistálého pouze pokud raycast zasáhne zem
        /// a vertikální rychlost je záporná nebo nulová (padání nebo stání).
        /// </summary>
        private void CheckGround()
        {
            Vector3 origin = transform.position;
            float distance = 2.5f;

            bool hit = Physics.Raycast(origin, Vector3.down, out RaycastHit _, distance);

            // Původní podmínka zachovaná doslova: zem se uzná až když hráč nestoupá,
            // ale jednou uznaná drží, dokud paprsek něco vidí. Výsledek jde do
            // groundProbe – o tom, jestli hráč SKUTEČNĚ stojí, rozhoduje až stav
            // (ve vodě ani při letu se zem nepočítá, i kdyby ji paprsek viděl).
            groundProbe = hit && (groundProbe || rb == null || rb.linearVelocity.y <= 0f);

            Debug.DrawRay(origin, Vector3.down * distance, hit ? Color.green : Color.red);
        }

        /// <summary>
        /// Aplikuje impuls skoku na Rigidbody hráče.
        /// Pokud je hráč ve dřepu a používá toggle režim, nejprve se postaví.
        /// </summary>
        private void Jump()
        {
            // Ve vodě klávesa skoku plave nahoru (viz SwimMotor), skok se neprovádí.
            if (moveState != PlayerMoveState.Grounded) return;

            rb.AddForce(0f, jumpPower, 0f, ForceMode.Impulse);

            // Odlepení se musí promítnout hned: jinak by CheckGround ve stejném snímku
            // zem ještě viděl a GroundAirMotor by nově získanou svislou rychlost
            // přilepením k zemi zase srazil.
            groundProbe = false;
            SetState(PlayerMoveState.Airborne);

            if (isCrouched && !holdToCrouch)
            {
                Crouch();
            }
        }

        /// <summary>
        /// Přepíná stav dřepu: mění Y scale hráče a rychlost chůze.
        /// Ve dřepu: scale se sníží na crouchHeight, rychlost se vynásobí speedReduction.
        /// Při vstávání: scale se obnoví, rychlost se vydělí speedReduction.
        /// </summary>
        private void Crouch()
        {
            if (isCrouched)
            {
                transform.localScale = new Vector3(originalScale.x, originalScale.y, originalScale.z);
                crouchSpeedMultiplier = 1f;
                isCrouched = false;
            }
            else
            {
                transform.localScale = new Vector3(originalScale.x, crouchHeight, originalScale.z);
                crouchSpeedMultiplier = speedReduction;
                isCrouched = true;
            }
        }

        /// <summary>
        /// Rychlost pohybu po zemi po započtení dřepu. Serializovaný <see cref="walkSpeed"/>
        /// ani <see cref="sprintSpeed"/> se nikdy nemění – mění se jen tahle odvozená hodnota.
        /// </summary>
        private float GetEffectiveSpeed(bool sprinting)
            => (sprinting ? sprintSpeed : walkSpeed) * crouchSpeedMultiplier;

        /// <summary>
        /// Aktuální stamina hráče v procentech (0-100).
        /// </summary>
        public float CurrentStamina
        {
            get
            {
                if (unlimitedSprint)
                    return 100f;

                return (sprintRemaining / sprintDuration) * 100f;
            }
        }

        /// <summary>
        /// Obnovuje staminu pouze při dostatku jídla, vody a zdraví.
        /// Ve stoji se obnovuje plnou rychlostí, při chůzi polovičním tempem.
        /// </summary>
        private void RegenerateStamina()
        {
            if (unlimitedSprint || sprintRemaining >= sprintDuration || !CanRegenerateStamina())
                return;

            float regenMultiplier = isWalking ? walkingStaminaRegenMultiplier : 1f;
            sprintRemaining = Mathf.Clamp(sprintRemaining + staminaRegenPerSecond * regenMultiplier * Time.deltaTime, 0f, sprintDuration);
        }

        private bool CanRegenerateStamina()
        {
            if (!isGrounded || isSprinting)
                return false;

            if (playerNeeds == null)
                return true;

            return playerNeeds.food >= staminaRecoveryNeedsThreshold
                && playerNeeds.water >= staminaRecoveryNeedsThreshold
                && playerNeeds.health >= staminaRecoveryHealthThreshold;
        }

        /// <summary>
        /// Detekuje schod před hráčem pomocí dvou raycastů (spodní a horní).
        /// Pokud spodní ray zasáhne překážku a horní ray projde volně (nízká překážka),
        /// a sklon překážky je menší než 60°, aplikuje sílu nahoru pro plynulé přelezení.
        /// </summary>
        private void StepClimb()
        {
            if (!isGrounded) return;

            Vector3 dir = transform.forward;
            Vector3 origin = transform.position;

            float stepCheckDistance = 0.6f;

            if (Physics.Raycast(origin, dir, out RaycastHit lowerHit, stepCheckDistance))
            {
                Vector3 upperOrigin = origin + Vector3.up * EffectiveStepHeight;

                if (!Physics.Raycast(upperOrigin, dir, stepCheckDistance))
                {
                    float slope = Vector3.Angle(lowerHit.normal, Vector3.up);

                    if (slope < 60f)
                    {
                        rb.AddForce(Vector3.up * stepSmooth, ForceMode.VelocityChange);
                    }
                }
            }
        }

        /// <summary>
        /// Animuje houpání kamery při pohybu (head bob).
        /// Rychlost houpání se liší při sprintu, dřepu a normální chůzi.
        /// Při stání se kamera plynule vrátí do výchozí pozice.
        /// </summary>
        private void HeadBob()
        {
            if (isWalking)
            {
                if (isSprinting)
                {
                    timer += Time.deltaTime * (bobSpeed + sprintSpeed);
                }
                else if (isCrouched)
                {
                    timer += Time.deltaTime * (bobSpeed * speedReduction);
                }
                else
                {
                    timer += Time.deltaTime * bobSpeed;
                }
                joint.localPosition = new Vector3(jointOriginalPos.x + Mathf.Sin(timer) * bobAmount.x, jointOriginalPos.y + Mathf.Sin(timer) * bobAmount.y, jointOriginalPos.z + Mathf.Sin(timer) * bobAmount.z);
            }
            else
            {
                timer = 0;
                joint.localPosition = new Vector3(Mathf.Lerp(joint.localPosition.x, jointOriginalPos.x, Time.deltaTime * bobSpeed), Mathf.Lerp(joint.localPosition.y, jointOriginalPos.y, Time.deltaTime * bobSpeed), Mathf.Lerp(joint.localPosition.z, jointOriginalPos.z, Time.deltaTime * bobSpeed));
            }
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Vlastní Unity Editor pro FirstPersonController.
    /// Zobrazuje nastavení ve strukturovaných sekcích: Camera Setup, Movement, Sprint, Jump, Crouch, Flight, Head Bob.
    /// Využívá SerializedObject pro správné undo/redo a Prefab override funkce.
    /// </summary>
    [CustomEditor(typeof(FirstPersonController)), InitializeOnLoadAttribute]
    public class FirstPersonControllerEditor : Editor
    {
        FirstPersonController fpc;
        SerializedObject SerFPC;

        private void OnEnable()
        {
            fpc = (FirstPersonController)target;
            SerFPC = new SerializedObject(fpc);
        }

        public override void OnInspectorGUI()
        {
            SerFPC.Update();
            #region Camera Setup

            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            GUILayout.Label("Camera Setup", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));
            EditorGUILayout.Space();

            fpc.playerCamera = (Camera)EditorGUILayout.ObjectField(new GUIContent("Camera", "Camera attached to the controller."), fpc.playerCamera, typeof(Camera), true);
            fpc.fov = EditorGUILayout.Slider(new GUIContent("Field of View", "The camera's view angle. Changes the player camera directly."), fpc.fov, fpc.zoomFOV, 179f);
            fpc.cameraCanMove = EditorGUILayout.ToggleLeft(new GUIContent("Enable Camera Rotation", "Determines if the camera is allowed to move."), fpc.cameraCanMove);

            GUI.enabled = fpc.cameraCanMove;
            fpc.invertCamera = EditorGUILayout.ToggleLeft(new GUIContent("Invert Camera Rotation", "Inverts the up and down movement of the camera."), fpc.invertCamera);
            fpc.mouseSensitivity = EditorGUILayout.Slider(new GUIContent("Look Sensitivity", "Determines how sensitive the mouse movement is."), fpc.mouseSensitivity, .1f, 10f);
            fpc.maxLookAngle = EditorGUILayout.Slider(new GUIContent("Max Look Angle", "Determines the max and min angle the player camera is able to look."), fpc.maxLookAngle, 40, 90);
            GUI.enabled = true;

            fpc.lockCursor = EditorGUILayout.ToggleLeft(new GUIContent("Lock and Hide Cursor", "Turns off the cursor visibility and locks it to the middle of the screen."), fpc.lockCursor);

            fpc.crosshair = EditorGUILayout.ToggleLeft(new GUIContent("Auto Crosshair", "Determines if the basic crosshair will be turned on, and sets is to the center of the screen."), fpc.crosshair);

            if (fpc.crosshair)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(new GUIContent("Crosshair Image", "Sprite to use as the crosshair."));
                fpc.crosshairImage = (Sprite)EditorGUILayout.ObjectField(fpc.crosshairImage, typeof(Sprite), false);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                fpc.crosshairColor = EditorGUILayout.ColorField(new GUIContent("Crosshair Color", "Determines the color of the crosshair."), fpc.crosshairColor);
                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            #region Camera Zoom Setup

            GUILayout.Label("Zoom", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));

            fpc.enableZoom = EditorGUILayout.ToggleLeft(new GUIContent("Enable Zoom", "Determines if the player is able to zoom in while playing."), fpc.enableZoom);

            GUI.enabled = fpc.enableZoom;
            fpc.holdToZoom = EditorGUILayout.ToggleLeft(new GUIContent("Hold to Zoom", "Requires the player to hold the zoom key instead if pressing to zoom and unzoom."), fpc.holdToZoom);
            fpc.zoomKey = (KeyCode)EditorGUILayout.EnumPopup(new GUIContent("Zoom Key", "Determines what key is used to zoom."), fpc.zoomKey);
            fpc.suppressZoomWithPickaxe = EditorGUILayout.ToggleLeft(new GUIContent("Blokovat zoom s krumpáčem", "Když hráč drží krumpáč, pravé tlačítko staví terén a nezoomuje."), fpc.suppressZoomWithPickaxe);
            fpc.zoomFOV = EditorGUILayout.Slider(new GUIContent("Zoom FOV", "Determines the field of view the camera zooms to."), fpc.zoomFOV, .1f, fpc.fov);
            fpc.zoomStepTime = EditorGUILayout.Slider(new GUIContent("Step Time", "Determines how fast the FOV transitions while zooming in."), fpc.zoomStepTime, .1f, 10f);
            GUI.enabled = true;

            #endregion

            #endregion

            #region Movement Setup

            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            GUILayout.Label("Movement Setup", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));
            EditorGUILayout.Space();

            fpc.playerCanMove = EditorGUILayout.ToggleLeft(new GUIContent("Enable Player Movement", "Determines if the player is allowed to move."), fpc.playerCanMove);

            GUI.enabled = fpc.playerCanMove;
            fpc.walkSpeed = EditorGUILayout.Slider(new GUIContent("Walk Speed", "Determines how fast the player will move while walking."), fpc.walkSpeed, .1f, fpc.sprintSpeed);
            GUI.enabled = true;

            EditorGUILayout.Space();

            #region Sprint

            GUILayout.Label("Sprint", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));

            fpc.enableSprint = EditorGUILayout.ToggleLeft(new GUIContent("Enable Sprint", "Determines if the player is allowed to sprint."), fpc.enableSprint);

            GUI.enabled = fpc.enableSprint;
            fpc.unlimitedSprint = EditorGUILayout.ToggleLeft(new GUIContent("Unlimited Sprint", "Determines if 'Sprint Duration' is enabled. Turning this on will allow for unlimited sprint."), fpc.unlimitedSprint);
            fpc.sprintKey = (KeyCode)EditorGUILayout.EnumPopup(new GUIContent("Sprint Key", "Determines what key is used to sprint."), fpc.sprintKey);
            fpc.sprintSpeed = EditorGUILayout.Slider(new GUIContent("Sprint Speed", "Determines how fast the player will move while sprinting."), fpc.sprintSpeed, fpc.walkSpeed, 50f);

            fpc.sprintDuration = EditorGUILayout.Slider(new GUIContent("Sprint Duration", "Determines how long the player can sprint while unlimited sprint is disabled."), fpc.sprintDuration, 1f, 20f);
            fpc.staminaDrainPerSecond = EditorGUILayout.Slider(new GUIContent("Stamina Drain", "How many stamina seconds are drained per second while sprinting."), fpc.staminaDrainPerSecond, .1f, 10f);
            fpc.staminaRegenPerSecond = EditorGUILayout.Slider(new GUIContent("Stamina Regen", "How many stamina seconds are restored per second while standing still."), fpc.staminaRegenPerSecond, .1f, 10f);
            fpc.walkingStaminaRegenMultiplier = EditorGUILayout.Slider(new GUIContent("Walking Regen Multiplier", "Multiplier applied to stamina regeneration while walking."), fpc.walkingStaminaRegenMultiplier, 0f, 1f);
            fpc.staminaRecoveryNeedsThreshold = EditorGUILayout.Slider(new GUIContent("Recovery Needs Threshold", "Minimum food and water required before stamina can regenerate."), fpc.staminaRecoveryNeedsThreshold, 0f, 100f);
            fpc.staminaRecoveryHealthThreshold = EditorGUILayout.Slider(new GUIContent("Recovery Health Threshold", "Minimum health required before stamina can regenerate."), fpc.staminaRecoveryHealthThreshold, 0f, 100f);
            fpc.sprintCooldown = EditorGUILayout.Slider(new GUIContent("Sprint Cooldown", "Determines how long the recovery time is when the player runs out of sprint."), fpc.sprintCooldown, .1f, fpc.sprintDuration);

            fpc.sprintFOV = EditorGUILayout.Slider(new GUIContent("Sprint FOV", "Determines the field of view the camera changes to while sprinting."), fpc.sprintFOV, fpc.fov, 179f);
            fpc.sprintFOVStepTime = EditorGUILayout.Slider(new GUIContent("Step Time", "Determines how fast the FOV transitions while sprinting."), fpc.sprintFOVStepTime, .1f, 20f);

            fpc.useSprintBar = EditorGUILayout.ToggleLeft(new GUIContent("Use Sprint Bar", "Determines if the default sprint bar will appear on screen."), fpc.useSprintBar);

            if (fpc.useSprintBar)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.BeginHorizontal();
                fpc.hideBarWhenFull = EditorGUILayout.ToggleLeft(new GUIContent("Hide Full Bar", "Hides the sprint bar when sprint duration is full, and fades the bar in when sprinting. Disabling this will leave the bar on screen at all times when the sprint bar is enabled."), fpc.hideBarWhenFull);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(new GUIContent("Bar BG", "Object to be used as sprint bar background."));
                fpc.sprintBarBG = (Image)EditorGUILayout.ObjectField(fpc.sprintBarBG, typeof(Image), true);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(new GUIContent("Bar", "Object to be used as sprint bar foreground."));
                fpc.sprintBar = (Image)EditorGUILayout.ObjectField(fpc.sprintBar, typeof(Image), true);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                fpc.sprintBarWidthPercent = EditorGUILayout.Slider(new GUIContent("Bar Width", "Determines the width of the sprint bar."), fpc.sprintBarWidthPercent, .1f, .5f);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                fpc.sprintBarHeightPercent = EditorGUILayout.Slider(new GUIContent("Bar Height", "Determines the height of the sprint bar."), fpc.sprintBarHeightPercent, .001f, .025f);
                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel--;
            }
            GUI.enabled = true;

            EditorGUILayout.Space();

            #endregion

            #region Jump

            GUILayout.Label("Jump", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));

            fpc.enableJump = EditorGUILayout.ToggleLeft(new GUIContent("Enable Jump", "Determines if the player is allowed to jump."), fpc.enableJump);

            GUI.enabled = fpc.enableJump;
            fpc.jumpKey = (KeyCode)EditorGUILayout.EnumPopup(new GUIContent("Jump Key", "Determines what key is used to jump."), fpc.jumpKey);
            fpc.jumpPower = EditorGUILayout.Slider(new GUIContent("Jump Power", "Determines how high the player will jump."), fpc.jumpPower, .1f, 200f);
            GUI.enabled = true;

            EditorGUILayout.Space();

            #endregion

            #region Crouch

            GUILayout.Label("Crouch", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));

            fpc.enableCrouch = EditorGUILayout.ToggleLeft(new GUIContent("Enable Crouch", "Determines if the player is allowed to crouch."), fpc.enableCrouch);

            GUI.enabled = fpc.enableCrouch;
            fpc.holdToCrouch = EditorGUILayout.ToggleLeft(new GUIContent("Hold To Crouch", "Requires the player to hold the crouch key instead if pressing to crouch and uncrouch."), fpc.holdToCrouch);
            fpc.crouchKey = (KeyCode)EditorGUILayout.EnumPopup(new GUIContent("Crouch Key", "Determines what key is used to crouch."), fpc.crouchKey);
            fpc.crouchHeight = EditorGUILayout.Slider(new GUIContent("Crouch Height", "Determines the y scale of the player object when crouched."), fpc.crouchHeight, .1f, 1);
            fpc.speedReduction = EditorGUILayout.Slider(new GUIContent("Speed Reduction", "Determines the percent 'Walk Speed' is reduced by. 1 being no reduction, and .5 being half."), fpc.speedReduction, .1f, 1);
            GUI.enabled = true;

            #endregion

            #region Flight
            EditorGUILayout.Space();
            GUILayout.Label("Flight", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));
            fpc.enableFlight = EditorGUILayout.ToggleLeft(new GUIContent("Enable Flight", "Determines if the player is allowed to fly."), fpc.enableFlight);

            GUI.enabled = fpc.enableFlight;
            fpc.flightSpeed = EditorGUILayout.Slider(new GUIContent("Flight Speed", "Determines how fast the player will move while flying."), fpc.flightSpeed, 1f, 500f);
            fpc.flightKey = (KeyCode)EditorGUILayout.EnumPopup(new GUIContent("Flight Key", "Determines what key is used to fly."), fpc.flightKey);
            GUI.enabled = true;

            #endregion

            #region Swimming

            // Sekce je PŘIDANÁ na konec; žádný existující řádek inspektoru se neměnil.
            EditorGUILayout.Space();
            GUILayout.Label("Swimming", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));

            fpc.enableSwimming = EditorGUILayout.ToggleLeft(new GUIContent("Enable Swimming", "Pokud je vypnuto, hráč vodu ignoruje a chová se jako dřív."), fpc.enableSwimming);

            GUI.enabled = fpc.enableSwimming;
            fpc.chestBelowEye = EditorGUILayout.Slider(new GUIContent("Chest Below Eye", "O kolik metrů níž než oči leží hrudník. Podle hrudníku se pozná vstup do vody."), fpc.chestBelowEye, 0.1f, 1.5f);
            fpc.fallbackEyeHeight = EditorGUILayout.Slider(new GUIContent("Fallback Eye Height", "Výška očí nad pivotem, použitá jen když není přiřazena kamera."), fpc.fallbackEyeHeight, 0.5f, 2.5f);
            fpc.swimEnterDepth = EditorGUILayout.Slider(new GUIContent("Enter Depth", "Jak hluboko pod hladinou musí být hrudník, aby plavání začalo."), fpc.swimEnterDepth, 0.02f, 1f);
            fpc.swimExitMargin = EditorGUILayout.Slider(new GUIContent("Exit Margin", "O kolik výš nad hladinu se musí hrudník dostat, aby plavání skončilo (hystereze)."), fpc.swimExitMargin, 0.01f, 1f);
            fpc.swimSpeed = EditorGUILayout.Slider(new GUIContent("Swim Speed", "Rychlost plavání."), fpc.swimSpeed, 0.5f, 10f);
            fpc.swimSprintSpeed = EditorGUILayout.Slider(new GUIContent("Swim Sprint Speed", "Rychlost plavání se sprintem."), fpc.swimSprintSpeed, fpc.swimSpeed, 12f);
            fpc.swimVerticalSpeed = EditorGUILayout.Slider(new GUIContent("Vertical Speed", "Rychlost stoupání a klesání ve vodě."), fpc.swimVerticalSpeed, 0.5f, 8f);
            fpc.swimAcceleration = EditorGUILayout.Slider(new GUIContent("Acceleration", "Zrychlení ve vodě v m/s². Nižší = těžkopádnější rozjezd."), fpc.swimAcceleration, 1f, 40f);
            fpc.swimDrag = EditorGUILayout.Slider(new GUIContent("Water Drag", "Hydrodynamický odpor po dobu plavání (Rigidbody.linearDamping)."), fpc.swimDrag, 0f, 15f);
            fpc.swimBuoyancy = EditorGUILayout.Slider(new GUIContent("Buoyancy", "Vztlakové zrychlení při plném ponoru. Musí být větší než gravitace."), fpc.swimBuoyancy, 0f, 40f);
            fpc.buoyancySpan = EditorGUILayout.Slider(new GUIContent("Buoyancy Span", "Hloubka ponoru hrudníku, při které je vztlak plný."), fpc.buoyancySpan, 0.1f, 3f);
            fpc.swimEntryDamping = EditorGUILayout.Slider(new GUIContent("Entry Damping", "Kolik svislé rychlosti zůstane po dopadu do vody."), fpc.swimEntryDamping, 0f, 1f);
            fpc.swimLookDiveAngle = EditorGUILayout.Slider(new GUIContent("Look Dive Angle", "Od jakého sklonu pohledu se i u hladiny plave volně ve třech osách."), fpc.swimLookDiveAngle, 5f, 89f);
            fpc.shoreClimbAssist = EditorGUILayout.Slider(new GUIContent("Shore Climb Assist", "Svislý přírůstek rychlosti při výlezu na břeh."), fpc.shoreClimbAssist, 0f, 3f);
            GUI.enabled = true;

            #endregion

            #region Step Climb

            // stepHeight dřív v inspektoru vůbec nebyl, takže se zděděná hodnota 10 m
            // nedala ani najít, ani opravit. Rozsah slideru ji srazí do smysluplných mezí.
            EditorGUILayout.Space();
            GUILayout.Label("Step Climb", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));

            fpc.stepHeight = EditorGUILayout.Slider(new GUIContent("Step Height", "Maximální výška schodu, přes který hráč přejde bez skoku. Vyšší hodnoty dělají z chůze lezení po skalách."), fpc.stepHeight, 0.05f, 1.5f);
            fpc.stepSmooth = EditorGUILayout.Slider(new GUIContent("Step Smooth", "Síla, kterou se hráč přes schod nadzvedne."), fpc.stepSmooth, 0.1f, 5f);

            #endregion

            #region Climbing

            EditorGUILayout.Space();
            GUILayout.Label("Climbing", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));

            fpc.enableClimbing = EditorGUILayout.ToggleLeft(new GUIContent("Enable Climbing", "Pokud je vypnuto, stěny se ignorují."), fpc.enableClimbing);

            GUI.enabled = fpc.enableClimbing;
            fpc.climbCheckDistance = EditorGUILayout.Slider(new GUIContent("Wall Check Distance", "Jak daleko před hráče sahá test stěny."), fpc.climbCheckDistance, 0.2f, 2f);
            fpc.climbProbeRadius = EditorGUILayout.Slider(new GUIContent("Probe Radius", "Poloměr SphereCastu hledajícího stěnu."), fpc.climbProbeRadius, 0.05f, 1f);
            fpc.minWallAngle = EditorGUILayout.Slider(new GUIContent("Min Wall Angle", "Minimální sklon plochy, aby se počítala za stěnu."), fpc.minWallAngle, 40f, 90f);
            fpc.maxWallAngle = EditorGUILayout.Slider(new GUIContent("Max Wall Angle", "Maximální sklon plochy. Nad 90° jde o převis."), fpc.maxWallAngle, fpc.minWallAngle, 130f);
            fpc.climbSpeed = EditorGUILayout.Slider(new GUIContent("Climb Speed", "Rychlost pohybu po stěně."), fpc.climbSpeed, 0.2f, 8f);
            fpc.climbAcceleration = EditorGUILayout.Slider(new GUIContent("Climb Acceleration", "Zrychlení na stěně."), fpc.climbAcceleration, 1f, 50f);
            fpc.climbStickForce = EditorGUILayout.Slider(new GUIContent("Stick Force", "Přítlak ke stěně. Bez něj se hráč od stěny odlepí."), fpc.climbStickForce, 0f, 30f);
            fpc.climbDrag = EditorGUILayout.Slider(new GUIContent("Climb Drag", "Odpor při lezení. Drží hráče na místě, když pustí klávesy."), fpc.climbDrag, 0f, 20f);
            fpc.climbStaminaDrainPerSecond = EditorGUILayout.Slider(new GUIContent("Stamina Drain", "Kolik sekund staminy ubude za sekundu lezení."), fpc.climbStaminaDrainPerSecond, 0f, 10f);
            fpc.climbMinStamina = EditorGUILayout.Slider(new GUIContent("Min Stamina To Start", "Kolik staminy musí zbývat, aby šlo lezení začít."), fpc.climbMinStamina, 0f, 5f);
            fpc.climbJumpOffPower = EditorGUILayout.Slider(new GUIContent("Jump Off Power", "Síla odrazu od stěny klávesou skoku."), fpc.climbJumpOffPower, 0f, 15f);
            fpc.climbCooldown = EditorGUILayout.Slider(new GUIContent("Cooldown", "Jak dlouho po opuštění stěny nejde lezení znovu začít."), fpc.climbCooldown, 0f, 3f);
            fpc.climbStallSpeed = EditorGUILayout.Slider(new GUIContent("Stall Speed", "Pod touhle rychlostí se hráč považuje za zaseknutého o stěnu."), fpc.climbStallSpeed, 0.05f, 3f);
            fpc.climbStallTime = EditorGUILayout.Slider(new GUIContent("Stall Time", "Jak dlouho musí zaseknutí trvat, než se ze země začne lézt."), fpc.climbStallTime, 0f, 1.5f);

            EditorGUILayout.Space();
            GUILayout.Label("Mantling", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Italic }, GUILayout.ExpandWidth(true));
            fpc.mantleProbeRise = EditorGUILayout.Slider(new GUIContent("Probe Rise", "O kolik výš než oči začíná paprsek hledající hranu."), fpc.mantleProbeRise, 0.1f, 2f);
            fpc.mantleForwardOffset = EditorGUILayout.Slider(new GUIContent("Forward Offset", "Jak daleko za stěnu se hledá plocha k vytažení."), fpc.mantleForwardOffset, 0.1f, 2f);
            fpc.mantleMaxGroundAngle = EditorGUILayout.Slider(new GUIContent("Max Ground Angle", "Do jakého sklonu se plocha nad hranou považuje za schůdnou."), fpc.mantleMaxGroundAngle, 5f, 80f);
            fpc.mantleDuration = EditorGUILayout.Slider(new GUIContent("Duration", "Jak dlouho trvá vytažení na římsu."), fpc.mantleDuration, 0.1f, 2f);
            fpc.mantleApexRise = EditorGUILayout.Slider(new GUIContent("Apex Rise", "O kolik nad římsu se hráč nejdřív zvedne."), fpc.mantleApexRise, 0f, 1f);
            fpc.mantleClearanceRadius = EditorGUILayout.Slider(new GUIContent("Clearance Radius", "Poloměr kontroly volného místa na římse."), fpc.mantleClearanceRadius, 0.1f, 1f);
            fpc.climbTopProbeDrop = EditorGUILayout.Slider(new GUIContent("Top Probe Drop", "O kolik níž než hrudník se hledá stěna podruhé. Rozhoduje o tom, jestli hráč u hrany spadne, nebo se vytáhne."), fpc.climbTopProbeDrop, 0.1f, 1.5f);
            GUI.enabled = true;

            #endregion

            #endregion

            #region Head Bob

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            GUILayout.Label("Head Bob Setup", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));
            EditorGUILayout.Space();

            fpc.enableHeadBob = EditorGUILayout.ToggleLeft(new GUIContent("Enable Head Bob", "Determines if the camera will bob while the player is walking."), fpc.enableHeadBob);

            GUI.enabled = fpc.enableHeadBob;
            fpc.joint = (Transform)EditorGUILayout.ObjectField(new GUIContent("Camera Joint", "Joint object position is moved while head bob is active."), fpc.joint, typeof(Transform), true);
            fpc.bobSpeed = EditorGUILayout.Slider(new GUIContent("Speed", "Determines how often a bob rotation is completed."), fpc.bobSpeed, 1, 20);
            fpc.bobAmount = EditorGUILayout.Vector3Field(new GUIContent("Bob Amount", "Determines the amount the joint moves in both directions on every axes."), fpc.bobAmount);
            GUI.enabled = true;

            #endregion

            if (GUI.changed)
            {
                EditorUtility.SetDirty(fpc);
                Undo.RecordObject(fpc, "FPC Change");
                SerFPC.ApplyModifiedProperties();
            }
        }
    }

#endif
}
