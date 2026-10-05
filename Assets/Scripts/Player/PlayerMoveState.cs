using UnityEngine;

namespace Orivilon.Player
{
    /// <summary>
    /// Stav pohybu hráče. Právě jeden je aktivní a rozhoduje o tom, který motor
    /// v <see cref="FirstPersonController"/> smí sáhnout na Rigidbody.
    ///
    /// <para>Důvod, proč tohle vzniklo: pohyb byl řízen sadou nezávislých boolů
    /// (<c>isGrounded</c>, <c>isFlying</c>, <c>isCrouched</c>, …) a gravitace se
    /// zapínala a vypínala na několika místech naráz – v <c>Update</c> i
    /// v <c>FixedUpdate</c>. S přibývajícími stavy (plavání, šplhání) se takový
    /// zápis nedá udržet: dva stavy si přepisují <c>rb.useGravity</c> ve stejném
    /// snímku a výsledek závisí na pořadí. Enum ten konflikt odstraňuje tím,
    /// že ho nejde vyjádřit.</para>
    ///
    /// <para>Podtyp <see cref="byte"/> je záměr: hodnota se posílá po síti
    /// vzdáleným hráčům (viz <c>Orivilon.Multiplayer.RemotePlayerController</c>),
    /// takže musí být levná a stabilní. Pořadí hodnot proto NEMĚŇ – přidávej
    /// jen na konec, jinak se rozejde význam stavu mezi klienty různých verzí.</para>
    /// </summary>
    public enum PlayerMoveState : byte
    {
        /// <summary>Hráč stojí nebo jde po zemi. Plná gravitace, žádný dodatečný odpor.</summary>
        Grounded = 0,

        /// <summary>Volný pád nebo skok. Gravitace navíc násobená <c>gravityMultiplier</c>.</summary>
        Airborne = 1,

        /// <summary>Hráč je ve vodě. Gravitaci nahrazuje vztlak, přibývá hydrodynamický odpor.</summary>
        Swimming = 2,

        /// <summary>
        /// Šplhání / bouldering po stěně. Gravitace je vypnutá, hráče drží přítlak
        /// ke stěně a zvýšený odpor; stojí to staminu a při jejím vyčerpání hráč padá.
        /// Vytažení přes horní hranu (mantling) je součástí tohohle stavu a končí
        /// přechodem do <see cref="Grounded"/>.
        /// </summary>
        Climbing = 3,

        /// <summary>Debug/admin létání. Gravitace vypnutá, rychlost se nastavuje přímo.</summary>
        Flying = 4,
    }

    /// <summary>
    /// Jeden snímek hráčova ÚMYSLU, odečtený ze vstupu a zbavený závislosti na klávesách.
    ///
    /// <para>Mezikrok mezi vstupem a fyzikou. Motory (<c>GroundAirMotor</c>,
    /// <c>SwimMotor</c>, <c>FlightMotor</c>) čtou jen tuhle strukturu, takže se
    /// nikde v <c>FixedUpdate</c> neptáme na <c>Input.GetKey</c>. Dva praktické
    /// důsledky: fyzikální snímek nevidí stisk, který mezitím zanikl, a stejný
    /// motor se dá pohánět daty ze sítě nebo z nahrávky, aniž by se musel měnit.</para>
    /// </summary>
    public readonly struct MoveIntent
    {
        /// <summary>Osy pohybu v rozsahu −1..1 (x = do stran, y = dopředu).</summary>
        public readonly Vector2 Move;

        /// <summary>Drží klávesu skoku. Ve vodě znamená „plav nahoru".</summary>
        public readonly bool Up;

        /// <summary>Drží klávesu dřepu. Ve vodě znamená „potop se".</summary>
        public readonly bool Down;

        /// <summary>Drží klávesu sprintu.</summary>
        public readonly bool Sprint;

        public MoveIntent(Vector2 move, bool up, bool down, bool sprint)
        {
            Move = move;
            Up = up;
            Down = down;
            Sprint = sprint;
        }

        /// <summary>Je vůbec zadán nějaký směr v rovině? Řídí head bob a obnovu staminy.</summary>
        public bool HasPlanarInput => Move.x != 0f || Move.y != 0f;

        /// <summary>Svislá složka úmyslu: +1 nahoru, −1 dolů, 0 nic.</summary>
        public float Vertical => (Up ? 1f : 0f) - (Down ? 1f : 0f);

        /// <summary>Prázdný úmysl – používá se, když je vstup blokovaný.</summary>
        public static MoveIntent None => new MoveIntent(Vector2.zero, false, false, false);
    }
}
