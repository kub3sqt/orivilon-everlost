using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.UI.HUD
{
    /// <summary>
    /// Oznaci libovolny objekt ve svete jako cil zobrazeny na kompasu (<see cref="CompassUI"/>).
    /// Komponenta se sama registruje v OnEnable a odregistruje v OnDisable, takze kompas
    /// nemusi nic hledat pres FindObjectsByType.
    ///
    /// Pouziti: gameObject.AddComponent&lt;CompassMarker&gt;() a nastavit barvu/popisek,
    /// pripadne pridat komponentu na prefab v Inspectoru.
    /// </summary>
    [DisallowMultipleComponent]
    public class CompassMarker : MonoBehaviour
    {
        /// <summary>Vsechny aktivni markery. Cte je <see cref="CompassUI"/> v LateUpdate.</summary>
        private static readonly List<CompassMarker> active = new List<CompassMarker>();

        /// <summary>Read-only pohled na registrovane markery.</summary>
        public static IReadOnlyList<CompassMarker> Active => active;

        [Header("Vzhled")]
        [Tooltip("Barva ikony na kompasu.")]
        [SerializeField] private Color color = new Color(1f, 0.62f, 0.15f, 1f);

        [Tooltip("Velikost ikony v pixelech.")]
        [SerializeField] private float iconSize = 18f;

        [Tooltip("Volitelna ikona. Pokud je null, pouzije se procedurálni kolecko.")]
        [SerializeField] private Sprite icon;

        [Header("Popisek")]
        [Tooltip("Text pod ikonou. Prazdny = nic se nevykresli.")]
        [SerializeField] private string label = string.Empty;

        [Tooltip("Zobrazit vzdalenost od hrace misto/pod popiskem.")]
        [SerializeField] private bool showDistance = true;

        [Header("Dosah")]
        [Tooltip("Nad touto vzdalenosti se marker na kompasu nezobrazi. 0 = bez limitu.")]
        [SerializeField] private float maxDistance = 0f;

        [Tooltip("Svisly offset bodu, ke kteremu se pocita smer (napr. stred postavy).")]
        [SerializeField] private Vector3 worldOffset = Vector3.zero;

        // ------------------------------------------------------------------ Verejne API

        public Color Color { get => color; set => color = value; }
        public float IconSize { get => iconSize; set => iconSize = value; }
        public Sprite Icon { get => icon; set => icon = value; }
        public string Label { get => label; set => label = value; }
        public bool ShowDistance { get => showDistance; set => showDistance = value; }
        public float MaxDistance { get => maxDistance; set => maxDistance = value; }

        /// <summary>Bod ve svete, ke kteremu kompas pocita azimut.</summary>
        public Vector3 WorldPosition => transform.position + worldOffset;

        // ------------------------------------------------------------------ Registrace

        private void OnEnable()
        {
            if (!active.Contains(this))
                active.Add(this);
        }

        private void OnDisable()
        {
            active.Remove(this);
        }
    }
}
