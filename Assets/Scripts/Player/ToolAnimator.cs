using UnityEngine;

namespace Orivilon.Player
{
    /// <summary>
    /// Ovládá Animator nástroje v ruce hráče.
    /// Nastavuje bool parametr "Holding" pro přechod do animace sekání/těžení.
    /// V momentu dopadu nástroje (Animation Event) deleguje zásah na PlayerInteraction.
    /// </summary>
    public class ToolAnimator : MonoBehaviour
    {
        /// <summary>Animator komponenta nástroje (přiřadit v Inspektoru).</summary>
        [SerializeField] private Animator animator;

        /// <summary>Příznak, zda hráč momentálně drží stisknuté levé tlačítko myši.</summary>
        private bool isHolding;

        /// <summary>
        /// Nastaví bool parametr "Holding" v Animatoru.
        /// Volá se z PlayerInteraction při stisku/uvolnění levého tlačítka myši.
        /// </summary>
        /// <param name="holding">True = LMB je stisknuto, False = LMB bylo uvolněno.</param>
        public void SetHolding(bool holding)
        {
            isHolding = holding;
            animator.SetBool("Holding", holding);
        }

        /// <summary>
        /// Animation Event – automaticky voláno animačním systémem v momentu dopadu nástroje.
        /// Pokud hráč stále drží LMB, deleguje zásah na PlayerInteraction.OnToolHit().
        /// </summary>
        public void OnToolHit()
        {
            if (!isHolding)
                return;

            PlayerInteraction.Instance?.OnToolHit();
        }
    }
}