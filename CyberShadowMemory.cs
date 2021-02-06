using System;
using System.Collections.Generic;
using System.Text;
using Voxif.AutoSplitter;
using Voxif.IO;
using Voxif.Memory;

namespace LiveSplit.CyberShadow {
    public class CyberShadowMemory : Memory {

        protected override string[] ProcessNames => new string[] { "CyberShadow" };

        //Used as a reference to simplify the offsets (first visible string in the page)
        private IntPtr StaticDataOffset;

        public StringPointer Mode { get; private set; }
        public StringPointer Level { get; private set; }
        public Pointer<double> Playtime { get; private set; }

        public Pointer<IntPtr> Activatables { get; private set; }
        private readonly HashSet<string> activatablesDone = new HashSet<string>();
        private int activatableCount = 0;

        public Pointer<IntPtr> Chapters { get; private set; }
        private readonly HashSet<string> chaptersDone = new HashSet<string>();
        private int chapterCount = 0;

        public CyberShadowMemory(Logger logger) : base(logger) {
            OnHook += () => {
                InitPointers();
            };
        }

        private void InitPointers() {
            //If the game is updated or have different versions,
            //switch to aobscan ((aob address)static->0x0->(aob offset)0x747D0(for playtime))
            //StaticDataOffset = game.Process.MainModule.BaseAddress + 0x32C2678;

            //switch to aobscan ((aob address)static->0x0->(aob offset)0x74830(for playtime))
            StaticDataOffset = game.Process.MainModule.BaseAddress + 0x32CBBE8;

            Mode = new UnionStringPointer(game, StaticDataOffset + 0x68);
            _ = Mode.New;

            Level = new UnionStringPointer(game, StaticDataOffset + 0x98);
            _ = Level.New;

            var ptrFactory = new NestedPointerFactory(game);

            Playtime = ptrFactory.Make<double>(StaticDataOffset + 0x470);

            var saveData = ptrFactory.Make<IntPtr>(StaticDataOffset + 0x9C8);
            logger.Log(saveData.New);
            Activatables = ptrFactory.Make<IntPtr>(saveData, 0x2058);
            //Chapters = ptrFactory.Make<IntPtr>(saveData, 0xAA50);
            Chapters = ptrFactory.Make<IntPtr>(saveData, 0xAA78);

            logger.Log(ptrFactory);
        }

        public void OnStart() {
            activatablesDone.Clear();
            activatableCount = 0;

            chaptersDone.Clear();
            chapterCount = 0;
        }

        public bool IsFlagNonZero(int offset) {
            return FlagValue(offset) != 0;
        }
        public double FlagValue(int offset) {
            return game.Read<double>(StaticDataOffset + offset);
        }

        public IEnumerable<string> NewActivatables() {
            int count = activatableCount;
            activatableCount = game.Read<int>(Activatables.New + 0xB0 + 0x8);
            if(count < activatableCount) {
                return NewLinkedList(Activatables.New, activatableCount, activatablesDone);
            }
            return Array.Empty<string>();
        }

        public IEnumerable<string> NewChapters() {
            int count = chapterCount;
            chapterCount = game.Read<int>(Chapters.New + 0xB0 + 0x8);
            if(count < chapterCount) {
                return NewLinkedList(Chapters.New, chapterCount, chaptersDone);
            }
            return Array.Empty<string>();
        }

        private IEnumerable<string> NewLinkedList(IntPtr pointer, int count, HashSet<string> done) {
            var newNames = new List<string>();
            int length = game.Read<int>(pointer + 0xB0 + 0x8 + 0x8);
            IntPtr arrayPtr = game.Read<IntPtr>(pointer + 0xB0 + 0x8 + 0xC);
            IntPtr firstPtr = game.Read<IntPtr>(arrayPtr + length * 4);
            for(int i = 0; i < count; i++) {
                string name = game.Read<UnionString>(firstPtr + 0x8).Value(game);
                if(done.Add(name)) {
                    newNames.Add(name);
                }
                firstPtr = game.Read<IntPtr>(firstPtr);
            }
            return newNames;
        }

        //Can't use StructLayout + FieldOffset as we need an unmanaged object
        private unsafe struct UnionString {
            private fixed byte pointerUnionString[16];
#pragma warning disable 0649
            private readonly int size;
            private readonly int flags;
#pragma warning restore 0649

            public unsafe string Value(ProcessWrapper wrapper) {
                fixed(byte* p = pointerUnionString) {
                    if(((flags >> 4) & 1) == 1) {
                        IntPtr pointer = (IntPtr)(wrapper.Is64Bit ? *(double*)p : *(int*)p);
                        return wrapper.ReadString(pointer, size, EStringType.UTF8);
                    } else {
                        return Encoding.UTF8.GetString(p, size);
                    }
                }
            }
        }

        public class UnionStringPointer : BaseStringPointer {

            public UnionStringPointer(TickableProcessWrapper wrapper, IntPtr basePtr, params int[] offsets)
                : this(wrapper, basePtr, EDerefType.Auto, offsets) { }
            public UnionStringPointer(TickableProcessWrapper wrapper, IntPtr basePtr, EDerefType derefType, params int[] offsets)
                : base(wrapper, basePtr, derefType, offsets) { }

            protected override void Update() {
                Old = (string)(newValue ?? default(string));
                New = wrapper.Read<UnionString>(derefType, Base, Offsets).Value(wrapper);
            }

            protected override IntPtr DerefOffsets() {
                return wrapper.Read(derefType, Base, Offsets);
            }
        }
    }
}