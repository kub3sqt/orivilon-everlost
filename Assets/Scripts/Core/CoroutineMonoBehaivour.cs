using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.Core
{
    /// <summary>
    /// Prázdný MonoBehaviour sloužící jako hostitelský objekt pro coroutiny spravované pøes CoroutineHelper.
    /// Vytváøí se automaticky jako nový GameObject s názvem "CoroutineParent", pokud ještì neexistuje.
    /// </summary>
    public class CoroutineMonoBehaviour : MonoBehaviour { }

    /// <summary>
    /// Statická pomocná tøída pro správu pojmenovaných coroutin.
    /// Umožòuje spouštìt, pøerušovat a sledovat coroutiny ze statického kontextu,
    /// kde není pøímo dostupný MonoBehaviour. Každá coroutina je identifikována øetìzcovým ID.
    /// </summary>
    public static class CoroutineHelper
    {
        /// <summary>
        /// Slovník aktivních coroutin mapující ID na instanci Coroutine.
        /// </summary>
        private static Dictionary<string, Coroutine> coroutines = new Dictionary<string, Coroutine>();

        /// <summary>
        /// Veøejný pøístup ke slovníku aktivních coroutin (pouze pro ètení externì).
        /// </summary>
        public static Dictionary<string, Coroutine> Coroutines { get { return coroutines; } }

        /// <summary>
        /// MonoBehaviour objekt, na kterém jsou coroutiny skuteènì spouštìny.
        /// </summary>
        private static CoroutineMonoBehaviour coroutineParent;

        /// <summary>
        /// Spustí coroutinu s daným ID. Pokud coroutina se stejným ID již bìží, nejprve ji zastaví.
        /// Pokud hostitelský objekt neexistuje, vytvoøí ho automaticky.
        /// </summary>
        /// <param name="routine">Metoda coroutiny k spuštìní.</param>
        /// <param name="id">Unikátní textové ID coroutiny.</param>
        /// <returns>Reference na spuštìnou Coroutine.</returns>
        public static Coroutine StartCoroutine(IEnumerator routine, string id)
        {
            if (coroutineParent == null)
            {
                coroutineParent = new GameObject("CoroutineParent").AddComponent<CoroutineMonoBehaviour>();
            }
            if (coroutines.ContainsKey(id))
            {
                StopCoroutine(id);
            }
            var coroutine = coroutineParent.StartCoroutine(routine);
            coroutines.Add(id, coroutine);
            return coroutine;
        }

        /// <summary>
        /// Zastaví coroutinu s daným ID a odstraní ji ze slovníku.
        /// Pokud coroutina s tímto ID neexistuje, metoda nic neprovede.
        /// </summary>
        /// <param name="id">ID coroutiny k zastavení.</param>
        public static void StopCoroutine(string id)
        {
            if (coroutines.ContainsKey(id))
            {
                var coroutine = coroutines[id];
                if (coroutine != null) coroutineParent.StopCoroutine(coroutine);
                coroutines.Remove(id);
            }
        }

        /// <summary>
        /// Zastaví coroutinu podle její instance a odstraní ji ze slovníku.
        /// Prohledá slovník a najde odpovídající záznam podle reference.
        /// </summary>
        /// <param name="coroutine">Instance Coroutine k zastavení.</param>
        public static void StopCoroutine(Coroutine coroutine)
        {
            foreach (var pair in coroutines)
            {
                if (pair.Value == coroutine)
                {
                    coroutines.Remove(pair.Key);
                    coroutineParent.StopCoroutine(coroutine);
                    return;
                }
            }
        }
    }
}