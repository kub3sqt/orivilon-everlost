using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Orivilon.Core;
using Orivilon.Player;

#if UNITY_EDITOR
using UnityEditor;
using System.Net;
#endif

namespace Orivilon.Player
{
    /// <summary>
    /// Kompletní ovladač hráče z pohledu první osoby.
    /// Řídí pohyb kamery (yaw/pitch), sprint s FOV efektem, skok, dřep, létání a head bob.
    /// Fyzika pohybu je realizována přes Rigidbody a AddForce (ne CharacterController).
    /// Vstup je blokován přes SceneLoader.InputBlocked během načítání.
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

        /// <summary>Maximální výška schodu, přes který hráč může přejít bez skoku.</summary>
        [Header("Step Climb")]
        public float stepHeight = 10f;

        /// <summary>Síla aplikovaná při přelézání schodu (ForceMode.VelocityChange).</summary>
        public float stepSmooth = 1f;

        /// <summary>Příznak, zda hráč aktuálně chodí (pro head bob).</summary>
        private bool isWalking = false;

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

        #endregion

        #region Jump

        /// <summary>Pokud true, skok je povolen.</summary>
        public bool enableJump = true;

        /// <summary>Klávesa pro skok.</summary>
        public KeyCode jumpKey = KeyCode.Space;

        /// <summary>Síla skoku aplikovaná jako Impulse na Rigidbody.</summary>
        public float jumpPower = 5f;

        /// <summary>Příznak, zda hráč stojí na zemi (detekováno raycastem dolů).</summary>
        private bool isGrounded = false;

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

        /// <summary>Příznak, zda hráč aktuálně létá.</summary>
        private bool isFlying = false;

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

        /// <summary>
        /// Inicializace: získá Rigidbody, nastaví FOV, uloží výchozí scale a pozici kloubu.
        /// Inicializuje sprint zbývající čas pokud sprint není neomezený.
        /// </summary>
        private void Awake()
        {
            rb = GetComponent<Rigidbody>();

            crosshairObject = GetComponentInChildren<Image>();

            playerCamera.fieldOfView = fov;
            originalScale = transform.localScale;
            jointOriginalPos = joint.localPosition;

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

            if (useSprintBar)
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
        private void Update()
        {
            if (SceneLoader.InputBlocked)
                return;

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
                if (Input.GetKeyDown(zoomKey) && !holdToZoom && !isSprinting)
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

                if (holdToZoom && !isSprinting)
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
                        sprintRemaining -= 1 * Time.deltaTime;
                        if (sprintRemaining <= 0)
                        {
                            isSprinting = false;
                            isSprintCooldown = true;
                        }
                    }
                }
                else
                {
                    sprintRemaining = Mathf.Clamp(sprintRemaining += 1 * Time.deltaTime, 0, sprintDuration);
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

                if (useSprintBar && !unlimitedSprint)
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

            if (enableCrouch)
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

            #region Flight
            if (enableFlight && Input.GetKeyDown(flightKey))
            {
                isFlying = !isFlying;
            }

            if (isFlying)
            {
                rb.useGravity = false;

                float flySpeed = flightSpeed;

                if (Input.GetKey(sprintKey))
                {
                    flySpeed *= 2;
                }

                if (Input.GetKey(KeyCode.LeftShift))
                {
                    rb.linearVelocity = -transform.up * flySpeed;
                }
                else if (Input.GetKey(KeyCode.Space))
                {
                    rb.linearVelocity = transform.up * flySpeed;
                }
                else
                {
                    rb.linearVelocity = Vector3.zero;
                }
            }
            else
            {
                rb.useGravity = true;
            }
            #endregion

            CheckGround();

            if (enableHeadBob)
            {
                HeadBob();
            }
        }

        /// <summary>
        /// Fyzikální snímek: aplikuje přidanou gravitaci, detekuje přelézání schodů,
        /// fixuje drobné klesání na zemi a vypočítává pohyb (chůze/sprint/let).
        /// Blokováno přes SceneLoader.InputBlocked.
        /// </summary>
        void FixedUpdate()
        {
            if (SceneLoader.InputBlocked)
                return;

            if (!isFlying && !isGrounded)
            {
                rb.AddForce(Physics.gravity * gravityMultiplier, ForceMode.Acceleration);
            }

            if (isGrounded && rb.linearVelocity.magnitude > 0.1f)
            {
                StepClimb();
            }

            if (isGrounded && rb.linearVelocity.y <= 0)
            {
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, -2f, rb.linearVelocity.z);
            }

            #region Movement

            if (playerCanMove)
            {
                Vector3 targetVelocity = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));

                if (targetVelocity.x != 0 || targetVelocity.z != 0 && isGrounded)
                {
                    isWalking = true;
                }
                else
                {
                    isWalking = false;
                }

                if (isFlying)
                {
                    targetVelocity = transform.TransformDirection(targetVelocity) * flightSpeed;
                    rb.linearVelocity = new Vector3(targetVelocity.x, rb.linearVelocity.y, targetVelocity.z);
                }
                else if (enableSprint && Input.GetKey(sprintKey) && sprintRemaining > 0f && !isSprintCooldown)
                {
                    targetVelocity = transform.TransformDirection(targetVelocity) * sprintSpeed;

                    Vector3 velocity = rb.linearVelocity;
                    Vector3 velocityChange = (targetVelocity - velocity);
                    velocityChange.x = Mathf.Clamp(velocityChange.x, -maxVelocityChange, maxVelocityChange);
                    velocityChange.z = Mathf.Clamp(velocityChange.z, -maxVelocityChange, maxVelocityChange);
                    velocityChange.y = 0;

                    if (velocityChange.x != 0 || velocityChange.z != 0)
                    {
                        isSprinting = true;

                        if (isCrouched)
                        {
                            Crouch();
                        }

                        if (hideBarWhenFull && !unlimitedSprint)
                        {
                            sprintBarCG.alpha += 5 * Time.deltaTime;
                        }
                    }

                    rb.AddForce(velocityChange, ForceMode.VelocityChange);
                }
                else
                {
                    isSprinting = false;

                    if (hideBarWhenFull && sprintRemaining == sprintDuration)
                    {
                    }

                    targetVelocity = transform.TransformDirection(targetVelocity) * walkSpeed;

                    Vector3 velocity = rb.linearVelocity;
                    Vector3 velocityChange = (targetVelocity - velocity);
                    velocityChange.x = Mathf.Clamp(velocityChange.x, -maxVelocityChange, maxVelocityChange);
                    velocityChange.z = Mathf.Clamp(velocityChange.z, -maxVelocityChange, maxVelocityChange);
                    velocityChange.y = 0;

                    rb.AddForce(velocityChange, ForceMode.VelocityChange);
                }
            }

            #endregion
        }

        /// <summary>
        /// Detekuje přítomnost země pod hráčem raycastem dolů.
        /// Hráč je považován za přistálého pouze pokud raycast zasáhne zem
        /// a vertikální rychlost je záporná nebo nulová (padání nebo stání).
        /// </summary>
        private void CheckGround()
        {
            Vector3 origin = transform.position;
            float distance = 2.5f;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, distance))
            {
                Debug.DrawRay(origin, Vector3.down * distance, Color.green);
                if (!isGrounded && rb.linearVelocity.y <= 0)
                {
                    isGrounded = true;
                }
            }
            else
            {
                Debug.DrawRay(origin, Vector3.down * distance, Color.red);
                isGrounded = false;
            }
        }

        /// <summary>
        /// Aplikuje impuls skoku na Rigidbody hráče.
        /// Pokud je hráč ve dřepu a používá toggle režim, nejprve se postaví.
        /// </summary>
        private void Jump()
        {
            if (isGrounded)
            {
                rb.AddForce(0f, jumpPower, 0f, ForceMode.Impulse);
                isGrounded = false;
            }

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
                walkSpeed /= speedReduction;

                isCrouched = false;
            }
            else
            {
                transform.localScale = new Vector3(originalScale.x, crouchHeight, originalScale.z);
                walkSpeed *= speedReduction;

                isCrouched = true;
            }
        }

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
                Vector3 upperOrigin = origin + Vector3.up * stepHeight;

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