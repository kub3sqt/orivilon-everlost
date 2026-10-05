using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Orivilon.UI.Arcanum
{
    /// <summary>
    /// Jeden uzel ve stromu. Drag a kolečko záměrně neimplementuje – události
    /// probublají do <see cref="ArcanumViewport"/>, takže tažení z uzlu posouvá mapu
    /// a klik se po tažení nevyvolá (EventSystem zruší eligibleForClick).
    /// </summary>
    public class ArcanumNodeView : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerClickHandler,
        ISelectHandler, IDeselectHandler, ISubmitHandler
    {
        public ArcanumUI owner;
        public ArcanumNode node;
        public Image slot;
        public Image frame;
        public Image selection;
        public Image icon;

        private float hover, hoverTarget;
        private float baseScale = 1f;

        public void OnPointerEnter(PointerEventData e)
        {
            SetHover(1f);
            owner.OnNodeHover(this, e.position);
        }

        public void OnPointerMove(PointerEventData e) => owner.OnNodeHoverMove(this, e.position);

        public void OnPointerExit(PointerEventData e)
        {
            SetHover(0f);
            owner.OnNodeHoverExit(this);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || e.dragging) return;
            owner.OnNodeClick(this, e.position);
        }

        public void OnSelect(BaseEventData e)
        {
            if (e is PointerEventData) return;
            SetHover(1f);
            owner.OnNodeKeyboardFocus(this);
        }

        public void OnDeselect(BaseEventData e)
        {
            SetHover(0f);
            owner.OnNodeHoverExit(this);
        }

        public void OnSubmit(BaseEventData e) => owner.OnNodeClick(this, owner.NodeScreenPoint(this));

        public void SetBaseScale(float s)
        {
            baseScale = s;
            transform.localScale = Vector3.one * (baseScale * (1f + 0.12f * hover));
        }

        private void SetHover(float target)
        {
            hoverTarget = target;
            owner.AnimateHover(this);
        }

        /// <summary>
        /// Krok hover škálování – volá ho ArcanumUI jen pro právě animované uzly
        /// (komponenta musí zůstat zapnutá, jinak EventSystem neposílá pointer události).
        /// Vrací true, když je animace hotová.
        /// </summary>
        public bool StepHover(float dt)
        {
            hover = Mathf.MoveTowards(hover, hoverTarget, dt * 8f);
            transform.localScale = Vector3.one * (baseScale * (1f + 0.12f * Mathf.SmoothStep(0f, 1f, hover)));
            return Mathf.Approximately(hover, hoverTarget);
        }

        public void ResetHover()
        {
            hover = hoverTarget = 0f;
            transform.localScale = Vector3.one * baseScale;
        }
    }

    /// <summary>Plocha mapy: tažení posouvá, kolečko přibližuje kolem kurzoru, klik mimo uzel zavře připnutý detail.</summary>
    public class ArcanumViewport : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler, IPointerClickHandler
    {
        public ArcanumUI owner;

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) owner.BeginPan();
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) owner.Pan(e.delta);
        }

        public void OnEndDrag(PointerEventData e) => owner.EndPan();

        public void OnScroll(PointerEventData e) => owner.ZoomAt(e.position, e.scrollDelta.y);

        public void OnPointerClick(PointerEventData e)
        {
            if (!e.dragging && e.pointerPress == gameObject) owner.OnBackgroundClick();
        }
    }
}
