using UnityEngine;

namespace Orivilon.World.Objects
{
    /// <summary>
    /// Stav vyhozeného předmětu (vedle <see cref="PickupItem"/>): ochrana před okamžitým sebráním,
    /// krátký let a dosednutí. Po dosednutí je rigidbody kinematické, takže předmět nepropadne,
    /// když se kolem něj odstreamuje collider terénu. S hráčem nekoliduje (jen paprsek E ho najde).
    /// </summary>
    [DisallowMultipleComponent]
    public class DroppedItem : MonoBehaviour
    {
        public PickupItem Pickup { get; private set; }

        /// <summary>Čas (Time.time), od kterého lze předmět sebrat.</summary>
        public float pickupAllowedAt;

        private Rigidbody rb;
        private Transform billboard;
        private Vector3 spawnPos;
        private float spawnTime, stillTime;
        private bool settled, ignoredPlayer;

        private const float MaxFlightTime = 5f;

        /// <summary>Kolo MP 3: po <see cref="MaxFlightTime"/> se zastaví jen předmět, který se čehokoli dotýká (kutálí
        /// se/klouže po svahu). Padající předmět (bez kontaktu – letí z útesu) se dřív zmrazil ve vzduchu; teď
        /// padá dál a nejpozději po <see cref="MaxAirTime"/> se položí na zem pod sebou.</summary>
        private const float MaxAirTime = 20f;
        private float lastContact = -1f;

        // Kontakt v tomto/předchozím fyzikálním kroku (Time.fixedTime) – odskakující předmět se dotýká jen občas
        // a nesmí se zmrazit uprostřed odskoku.
        // Jen OPORA zespodu (normála kontaktu míří nahoru) – dotyk stěny/útesu při pádu se nepočítá, jinak by se
        // předmět zmrazil přilepený na svislé stěně.
        private void OnCollisionEnter(Collision c) => NoteContact(c);
        private void OnCollisionStay(Collision c) => NoteContact(c);
        private void NoteContact(Collision c)
        {
            // Opora z jiného ještě se pohybujícího dropu se nepočítá: zmrazení na kutálejícím se předmětu
            // nechalo kládu viset 9 m nad svahem, když podložka odjela dál (Drop QA, kolo MP 3).
            var other = c.rigidbody;
            if (other != null && !other.isKinematic) return;
            for (int i = 0; i < c.contactCount; i++)
                if (c.GetContact(i).normal.y > 0.3f) { lastContact = Time.fixedTime; return; }
        }

        public bool CanPickUp => Time.time >= pickupAllowedAt;
        public bool Settled => settled;

        /// <summary>Diagnostika (QA): proč předmět dosedl – „still“, „wedge“, „timeout“, „air“, „fall“, „init“, „net“.</summary>
        public string SettleReason { get; private set; } = "";
        public float SettledAfter { get; private set; }
        public float SettledAt { get; private set; }

        // ── Multiplayer (kolo MP 17.10.2026, viz NetworkWorldSync.Drops) ──
        /// <summary>Síťové ID přidělené hostem (u hosta při vytvoření, u klienta převzaté).</summary>
        public uint NetId { get; internal set; }
        /// <summary>Klientská replika hostova předmětu: sebrání jde přes žádost hostovi, neukládá se.</summary>
        public bool IsReplica { get; private set; }
        /// <summary>Host: kdo simuluje let (clientId toho, kdo vyhodil).</summary>
        public ulong SimOwner { get; set; }
        /// <summary>Host: místo vyhození (validace hlášeného dosednutí).</summary>
        public Vector3 NetworkSpawn { get; private set; }
        /// <summary>Host: čeká se na dosednutí od klienta, který předmět vyhodil.</summary>
        public bool AwaitingOwnerSettle { get; private set; }

        public void SetNetworkSpawn(Vector3 p)
        {
            NetworkSpawn = p;
            var nm = Unity.Netcode.NetworkManager.Singleton;
            AwaitingOwnerSettle = nm != null && nm.IsServer && SimOwner != nm.LocalClientId;
        }

        public void MakeReplica(uint netId) { NetId = netId; IsReplica = true; }

        /// <summary>Autoritativní poloha ze sítě: přesun a dosednutí bez dalšího hlášení.</summary>
        public void ApplyAuthoritativePose(Vector3 p, Quaternion r)
        {
            AwaitingOwnerSettle = false;
            if (rb != null && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            transform.SetPositionAndRotation(p, r);
            if (rb != null) { rb.isKinematic = true; rb.position = p; rb.rotation = r; }
            spawnPos = p;
            settled = true;
            SettleReason = "net";
            stillTime = 0f;
        }

        /// <summary>Kde předmět leží (při letu místo vyhození – save uprostřed letu nic neztratí).</summary>
        public Vector3 RestPosition => settled ? transform.position : spawnPos;

        public void Init(PickupItem pickup, Rigidbody body, Transform billboardVisual, float pickupDelay, bool alreadySettled)
        {
            Pickup = pickup;
            rb = body;
            billboard = billboardVisual;
            spawnPos = transform.position;
            spawnTime = Time.time;
            pickupAllowedAt = Time.time + Mathf.Max(0f, pickupDelay);
            if (alreadySettled) Settle(false, "init"); else if (rb != null) rb.isKinematic = false;
        }

        /// <summary>Krátký pohyb od hráče (jako hod v Minecraftu); koliduje se světem, ne s hráčem.</summary>
        public void Launch(Vector3 forward)
        {
            if (rb == null || settled) return;
            rb.isKinematic = false;
            rb.linearVelocity = forward * 2.5f + Vector3.up * 2f;
        }

        private void OnEnable()
        {
            ItemDropService.Register(this);
            IgnorePlayer();
        }

        private void OnDisable() => ItemDropService.Unregister(this);

        /// <summary>Hráč do předmětu nenaráží ani ho neodstrčí (Physics.IgnoreCollision jen pro tento pár).</summary>
        private void IgnorePlayer()
        {
            if (ignoredPlayer) return;
            var player = GameObject.FindWithTag("Player");
            var mine = GetComponent<Collider>();
            if (player == null || mine == null) return;
            foreach (var c in player.GetComponentsInChildren<Collider>(true))
                Physics.IgnoreCollision(mine, c, true);
            ignoredPlayer = true;
        }

        private void FixedUpdate()
        {
            if (settled || rb == null) return;
            if (!ignoredPlayer) IgnorePlayer();

            // Pojistka: propad skrz terén (díra v collideru, odstreamovaný collider) – pod předmětem není
            // nic. Kutálení z kopce (zem pod ním je) se neruší.
            if (transform.position.y < spawnPos.y - 6f &&
                !Physics.Raycast(transform.position, Vector3.down, 60f, GroundMask, QueryTriggerInteraction.Ignore))
            {
                // Kolo MP 3: místo vyhození leží ve vzduchu (před hráčem) – položit na zem pod ním, je-li tam
                // collider; jinak (collider odstreamovaný) zůstat kinematicky na místě vyhození jako dřív.
                var col = GetComponent<Collider>();
                float half = col != null ? col.bounds.extents.y : 0.3f;
                if (Physics.Raycast(spawnPos + Vector3.up * 2f, Vector3.down, out RaycastHit g, 120f, GroundMask, QueryTriggerInteraction.Ignore))
                    transform.position = g.point + Vector3.up * half;
                else transform.position = spawnPos;
                Settle(true, "fall");
                return;
            }

            bool still = rb.linearVelocity.sqrMagnitude < 0.04f && rb.angularVelocity.sqrMagnitude < 0.04f;
            stillTime = still ? stillTime + Time.fixedDeltaTime : 0f;
            float age = Time.time - spawnTime;
            bool supported = Time.fixedTime - lastContact <= Time.fixedDeltaTime * 1.01f;
            if (stillTime > 0.3f && supported) Settle(true, "still");
            else if (stillTime > 1f)
            {
                // Kolo MP 3: stojí, ale bez opory zespodu – zaklíněný bokem mezi balvanem a jiným dropem
                // (Drop QA: kláda 1,2–1,7 m nad zemí, průnik jen vodorovný). Spát/ztratit callbacky může i
                // předmět ležící na zemi, proto se jen dosedne tvarem collideru dolů, je-li pod ním mezera.
                Settle(true, SnapDown(3f) ? "wedge" : "still");
            }
            else if (age > MaxFlightTime && Time.fixedTime - lastContact <= Time.fixedDeltaTime * 1.01f)
            {
                // Kontakt byl, ale v tomto kroku může předmět odskakovat (pár cm nad svahem) – dosednout tvarem
                // collideru dolů na povrch, teprve pak zmrazit.
                SnapDown(2f);
                Settle(true, "timeout");
            }
            else if (age > MaxAirTime)
            {
                // Pojistka bez kontaktu: položit na nejbližší zem pod předmětem (jinak zpět na místo vyhození).
                var col = GetComponent<Collider>();
                float half = col != null ? col.bounds.extents.y : 0.3f;
                if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 400f, GroundMask, QueryTriggerInteraction.Ignore))
                    transform.position = hit.point + Vector3.up * half;
                else transform.position = spawnPos;
                Settle(true, "air");
            }
        }

        /// <summary>
        /// Dosednutí tvarem collideru dolů (nanejvýš maxDist). Sweep přehlíží collidery, ve kterých předmět už
        /// je (dotek svahu, šev dvou chunků), a mohl by ho tak protáhnout pod povrch – posun se proto přijme jen
        /// tehdy, když nad novým středem není žádná zem. Jinak zůstane, kde je.
        /// </summary>
        private bool SnapDown(float maxDist)
        {
            if (!rb.SweepTest(Vector3.down, out RaycastHit hit, maxDist, QueryTriggerInteraction.Ignore) || hit.distance <= 0.02f) return false;
            var col = GetComponent<Collider>();
            if (col == null) return false;
            float step = hit.distance - 0.01f;
            Vector3 c = col.bounds.center + Vector3.down * step;
            float up = col.bounds.extents.y + 2f;
            if (Physics.Raycast(c + Vector3.up * up, Vector3.down, up, GroundMask, QueryTriggerInteraction.Ignore)) return false;
            transform.position += Vector3.down * step;
            return true;
        }

        private static int groundMask;
        private static int GroundMask
        {
            get
            {
                if (groundMask == 0)
                {
                    groundMask = Physics.DefaultRaycastLayers;
                    foreach (var n in new[] { "Pickup", "Player" }) { int l = LayerMask.NameToLayer(n); if (l >= 0) groundMask &= ~(1 << l); }
                }
                return groundMask;
            }
        }

        /// <summary>Znovu zapne fyziku (podložka zmizela – sebraný předmět pod ním).</summary>
        public void Wake()
        {
            if (!settled || rb == null) return;
            // Replika na klientovi se sama nehýbe – polohu po probuzení pošle host (DropMove).
            if (IsReplica) return;
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm != null && nm.IsServer) { SimOwner = nm.LocalClientId; AwaitingOwnerSettle = false; }
            settled = false;
            stillTime = 0f;
            lastContact = -1f;
            spawnPos = transform.position;
            spawnTime = Time.time;
            rb.isKinematic = false;
        }

        private void Settle(bool report, string reason)
        {
            settled = true;
            SettleReason = reason;
            SettledAfter = Time.time - spawnTime;
            SettledAt = Time.time;
            if (rb != null)
            {
                if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                rb.isKinematic = true;
            }
            // Multiplayer: simulující strana pošle autoritativní pozici dosednutí (jinde nic nedělá).
            if (report) Orivilon.Multiplayer.NetworkWorldSync.Instance?.OnDropSettled(this);
        }

        private void LateUpdate()
        {
            if (billboard == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 d = cam.transform.position - billboard.position;
            d.y = 0f;
            if (d.sqrMagnitude > 1e-4f) billboard.rotation = Quaternion.LookRotation(-d.normalized, Vector3.up);
        }

        /// <summary>Předmět je celý sebraný: hned zmizí z registru (save ho už nezapíše) a zničí se.</summary>
        public void Consume()
        {
            if (Pickup != null) Pickup.itemCount = 0;
            // Předměty položené na tomhle by zůstaly viset ve vzduchu – vzbudit je.
            var col = GetComponent<Collider>();
            if (col != null && Application.isPlaying)
            {
                var b = col.bounds;
                foreach (var c in Physics.OverlapBox(b.center + Vector3.up * b.extents.y, b.extents + Vector3.one * 0.3f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                {
                    var other = c != col ? c.GetComponent<DroppedItem>() : null;
                    if (other != null && other != this) other.Wake();
                }
            }
            gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }
    }
}
