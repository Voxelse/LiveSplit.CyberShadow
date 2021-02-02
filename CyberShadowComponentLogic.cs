using System;
using System.Collections.Generic;

namespace LiveSplit.CyberShadow {
    using Offsets = HashSet<int>;
    using OffsetsValues = Dictionary<int, HashSet<double>>;

    public partial class CyberShadowComponent {

        private readonly Offsets bossOffsets = new Offsets();
        private readonly OffsetsValues bossOffsetsPhases = new OffsetsValues();

        private readonly Offsets weaponOffsets = new Offsets();
        private readonly OffsetsValues weaponOffsetsValues = new OffsetsValues();

        private readonly RemainingDictionary remainingSplits;

        public override bool Update() {
            return memory.Update();
        }

        public override bool Start() {
            return memory.Mode.Changed && memory.Mode.Old.Equals("title") && memory.Playtime.New == 0;
        }

        public override void OnStart() {
            memory.OnStart();
            
            bossOffsets.Clear();
            bossOffsetsPhases.Clear();
            weaponOffsets.Clear();
            weaponOffsetsValues.Clear();

            HashSet<string> splits = new HashSet<string>(settings.Splits);
            foreach(string split in settings.Splits) {
                if(split.StartsWith("Boss_")) {
                    AddSplitOffsetOrValue(split, 5, typeof(EBosses), bossOffsets, bossOffsetsPhases);
                } else if(split.StartsWith("Weapon_")) {
                    AddSplitOffsetOrValue(split, 7, typeof(EWeapons), weaponOffsets, weaponOffsetsValues);
                }
            }
            remainingSplits.Setup(splits);

            void AddSplitOffsetOrValue(string split, int substringCount, Type enumType, Offsets offsets, OffsetsValues offsetsValues) {
                string splitSetting = split.Substring(substringCount);
                int valueId = splitSetting.LastIndexOf('_');
                if(valueId == -1) {
                    offsets.Add((int)Enum.Parse(enumType, splitSetting));
                } else {
                    string spitName = splitSetting.Substring(0, valueId);
                    int offset = (int)Enum.Parse(enumType, spitName);
                    if(!offsetsValues.ContainsKey(offset)) {
                        offsetsValues.Add(offset, new HashSet<double>());
                    }
                    offsetsValues[offset].Add(Double.Parse(splitSetting.Substring(valueId + 1)));
                }
            }
        }

        public override bool Split() {
            return SplitChapter() || SplitActivatable() || SplitLevel() || SplitBoss() || SplitWeapon();

            bool SplitChapter() {
                if(!remainingSplits.ContainsKey("Chapter")) {
                    return false;
                }
                foreach(string name in memory.NewChapters()) {
                    if(remainingSplits.Split("Chapter", name)) {
                        return true;
                    }
                }
                return false;
            }

            bool SplitActivatable() {
                if(!remainingSplits.ContainsKey("Activatable")) {
                    return false;
                }
                foreach(string name in memory.NewActivatables()) {
                    if(remainingSplits.Split("Activatable", name)) {
                        return true;
                    }
                }
                return false;
            }

            bool SplitLevel() {
                return remainingSplits.ContainsKey("Level")
                    && memory.Level.Changed && remainingSplits.Split("Level", memory.Level.New);
            }

            bool SplitBoss() {
                return SplitFlag("Boss", typeof(EBosses), bossOffsets, bossOffsetsPhases);
            }

            bool SplitWeapon() {
                return SplitFlag("Weapon", typeof(EWeapons), weaponOffsets, weaponOffsetsValues);
            }

            bool SplitFlag(string type, Type enumType, Offsets offsets, OffsetsValues offsetsValues) {
                foreach(int offset in offsets) {
                    if(memory.IsFlagNonZero(offset)) {
                        logger.Log("Split " + type + " " + Enum.GetName(enumType, offset));
                        return offsets.Remove(offset);
                    }
                }
                foreach(KeyValuePair<int, HashSet<double>> kvp in offsetsValues) {
                    double value = memory.FlagValue(kvp.Key);
                    if(kvp.Value.Remove(value)) {
                        logger.Log("Split " + type + " " + Enum.GetName(enumType, kvp.Key) + " " + value);
                        if(kvp.Value.Count == 0) {
                            offsetsValues.Remove(kvp.Key);
                        }
                        return true;
                    }
                }
                return false;
            }
        }
        public override bool Reset() {
            return memory.Mode.Changed && memory.Mode.New.Equals("title");
        }

        public override TimeSpan? GameTime() {
            return TimeSpan.FromSeconds(memory.Playtime?.New ?? 0);
        }
    }
}