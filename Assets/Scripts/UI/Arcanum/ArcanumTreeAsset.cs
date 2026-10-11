using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.UI.Arcanum
{
    /// <summary>
    /// Serialized Arcanum research tree (Resources/Arcanum/ArcanumTree): the six disciplines and
    /// all 181 nodes with names, recipes, costs, parents and default positions. Node ids are the
    /// save-file keys – keep their order (root, then 30 nodes per discipline).
    /// Node positions shown in the game come from the ArcanumUI prefab (move the node objects
    /// there); the positions here are the defaults the prefab was authored from.
    /// </summary>
    [CreateAssetMenu(menuName = "Everlost/Arcanum Tree", fileName = "ArcanumTree")]
    public class ArcanumTreeAsset : ScriptableObject
    {
        [Serializable]
        public class Category
        {
            public string name;
            public Color color = Color.white;
            public string materialItemId;
            public string iconItemId;
            public string[] tierNames = new string[6];
            public string[] tierRecipes = new string[6];
        }

        [Serializable]
        public class Node
        {
            public int id;
            public int category;
            public int tier;
            public int parent;
            public bool major;
            public string name;
            public string recipe;
            public int materialCost;
            public Vector2 position;
        }

        public string rootIconItemId = "everlost:stone_pickaxe";
        public List<Category> categories = new List<Category>();
        public List<Node> nodes = new List<Node>();
    }
}
