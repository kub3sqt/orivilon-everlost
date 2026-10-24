using Orivilon.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Orivilon.Multiplayer
{
    /// <summary>Volitelný dodatek zprávy pozice (verze 1, beze změny formátu): rychlost, fáze kroku 0..1, stáří akce,
    /// pohybový stav, akce, id drženého předmětu, výška a pata kolize. Původní prefix zprávy zůstává.</summary>
    public struct TravelerNetworkPose
    {
        public float speed, phase, age, height, feet;
        public byte move, action;
        public FixedString64Bytes item;

        public static TravelerNetworkPose Capture()
        {
            var avatar = TravelerAvatar.Local;
            if (avatar == null) return default;
            string id = PlayerEquipment.HeldTool != null ? PlayerEquipment.HeldTool.id : "";
            return new TravelerNetworkPose { speed = avatar.Speed, phase = avatar.Phase,
                age = Mathf.Min(avatar.ActionAge, 10f), move = (byte)avatar.MoveState,
                height = avatar.BodyHeight, feet = avatar.FeetOffset, action = (byte)avatar.Action, item = System.Text.Encoding.UTF8.GetByteCount(id ?? "") <= 61 ? new FixedString64Bytes(id ?? "") : default };
        }

        public void Write(FastBufferWriter writer)
        {
            writer.WriteValueSafe((byte)1);
            writer.WriteValueSafe(speed); writer.WriteValueSafe(phase); writer.WriteValueSafe(age);
            writer.WriteValueSafe(move); writer.WriteValueSafe(action); writer.WriteValueSafe(item);
            writer.WriteValueSafe(height); writer.WriteValueSafe(feet);
        }

        public static TravelerNetworkPose Read(FastBufferReader reader)
        {
            // Older senders have no animation trailer.
            if (reader.Position == reader.Length) return default;
            reader.ReadValueSafe(out byte version);
            if (version != 1) return default;
            var pose = new TravelerNetworkPose();
            reader.ReadValueSafe(out pose.speed); reader.ReadValueSafe(out pose.phase); reader.ReadValueSafe(out pose.age);
            reader.ReadValueSafe(out pose.move); reader.ReadValueSafe(out pose.action); reader.ReadValueSafe(out pose.item);
            reader.ReadValueSafe(out pose.height); reader.ReadValueSafe(out pose.feet);
            if (float.IsNaN(pose.speed) || float.IsInfinity(pose.speed) ||
                float.IsNaN(pose.phase) || float.IsInfinity(pose.phase) ||
                float.IsNaN(pose.age) || float.IsInfinity(pose.age) ||
                float.IsNaN(pose.height) || float.IsInfinity(pose.height) ||
                float.IsNaN(pose.feet) || float.IsInfinity(pose.feet)) return default;
            // Rychlost je ve světových m/s: chůze 30, sprint 40, na svahu s vertikální složkou až ×1,5. Dřívější mez 30
            // usekla sprint klienta na rychlost chůze (host ho viděl kráčet krokem chůze; QA kolo 4). 80 = rozumná pojistka.
            pose.speed = Mathf.Clamp(pose.speed, 0f, 80f); pose.phase = Mathf.Repeat(pose.phase, Mathf.PI * 2f);
            pose.age = Mathf.Clamp(pose.age, 0f, 10f);
            if (pose.move > (byte)PlayerMoveState.Flying) pose.move = 0;
            if (pose.action > (byte)TravelerAction.Use) pose.action = 0;
            return pose;
        }

        public void Apply(RemotePlayerController remote)
        {
            if (remote == null) return;
            remote.SetMoveState(move);
            remote.GetComponent<TravelerAvatar>()?.ReceivePose(speed, phase, move, action, age, item, height <= 0f ? 8f : height, height <= 0f ? -2f : feet);
        }
    }
}
