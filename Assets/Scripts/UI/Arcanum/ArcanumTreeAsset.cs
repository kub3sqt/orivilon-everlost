using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.UI.Arcanum
{
    /// <summary>
    /// Serialized Arcanum research tree v3 (Resources/Arcanum/ArcanumTree). Generated from
    /// design/arcanum/data.js by the editor tool Tools/Everlost/Arcanum/Build tree asset from data.js –
    /// do not edit by hand. Node codes (e.g. "KO11") are the save-file keys; list order is only a
    /// runtime index. Disciplines keep the DISC order of data.js.
    /// </summary>
    [CreateAssetMenu(menuName = "Everlost/Arcanum Tree", fileName = "ArcanumTree")]
    public class ArcanumTreeAsset : ScriptableObject
    {
        /// <summary>Item id (already in the "everlost:&lt;key&gt;" scheme) and amount.</summary>
        [Serializable]
        public struct ItemAmount
        {
            public string itemId;
            public int amount;
        }

        [Serializable]
        public class Category
        {
            public string key;          // "ar", "ko", ...
            public string name;
            public Color color = Color.white;
            public string iconItemId;
            public string[] pathKeys;   // path letters ("J", "C", ...), same order as paths
            public string[] paths;      // path names
        }

        [Serializable]
        public class Node
        {
            public string code;         // "R0", "KO11"
            public int category;        // index into categories, -1 = root
            public string path;         // path letter within the discipline ("" for root)
            public int tier;            // 0-6
            public string kind;         // R research, D discovery, B blueprint, K keystone
            public string name;
            public string[] requires;   // node codes, all required
            public string[] any;        // node codes, at least one required (may be empty)
            public ItemAmount[] cost;   // research cost ($)
            public string quest;        // discovery trigger (q)
            public ItemAmount[] blueprint; // materials of the hand-built blueprint (bp)
            public string[] makes;      // item ids this node teaches to make (mk)
            public string unlock;       // unlock text (u)
            public string flavor;       // flavor text (fl)
            public string icon;         // icon key (ic)
        }

        /// <summary>Era seal required by every node of <see cref="tier"/> (tier &gt;= 2).</summary>
        [Serializable]
        public struct Seal
        {
            public int tier;
            public int total;   // researched nodes without the root
            public int disc;    // how many disciplines need at least `each`
            public int each;
        }

        public string rootIconItemId = "everlost:stone_pickaxe";
        public List<Category> categories = new List<Category>();
        public List<Node> nodes = new List<Node>();
        public string[] itemIds;        // every item id of ITEMS in data.js ("everlost:<key>"), for validation
        public List<Seal> seals = new List<Seal>();
    }
}
