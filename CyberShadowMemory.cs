using System;
using System.Collections.Generic;
using System.Text;
using Voxif.AutoSplitter;
using Voxif.Helpers.MemoryHelper;
using Voxif.IO;
using Voxif.Memory;

namespace LiveSplit.CyberShadow {
    public class CyberShadowMemory : Memory {

        private const string ModuleName = "CyberShadow.exe";
        protected override string[] ProcessNames => new string[] { "CyberShadow" };

        private IntPtr StaticDataOffset;
        private const int SaveDictOffset = 0xB0 + 0x8;

        public StringPointer Mode { get; private set; }
        public StringPointer Level { get; private set; }
        public Pointer<double> Playtime { get; private set; }

        public Pointer<IntPtr> Activatables { get; private set; }
        private readonly HashSet<string> activatablesDone = new HashSet<string>();
        private int activatablesCount = 0;

        public Pointer<IntPtr> Chapters { get; private set; }
        private readonly HashSet<string> chaptersDone = new HashSet<string>();
        private int chaptersCount = 0;

        private ScanHelperTask scanTask;

        public CyberShadowMemory(Logger logger) : base(logger) {
            OnHook += () => {
                scanTask = new ScanHelperTask(game, logger);
                scanTask.Run(new ScannableData {
                    { ModuleName, new Dictionary<string, ScanTarget> {
                        { "gameData", new ScanTarget(0, "B9 ???????? C7 45 ?? 00 00 00 00 E8 ???????? 8B 4D") },
                        { "saveData", new ScanTarget(0x3, "C7 45 ?? ???? ???? C7 45 ?? ???? ???? B9 ???????? C7 45") },
                        { "saveDataOffset", new ScanTarget(0xD, "56 57 8D 75 ?? 8B 93 ???????? 8B 83") },
                    } },
                }, (result) => InitPointers(result));
            };
            OnExit += () => {
                if(scanTask != null) {
                    scanTask.Dispose();
                    scanTask = null;
                }
            };
        }

        private void InitPointers(ScannableResult result) {
            var ptrFactory = new NestedPointerFactory(game);

            IntPtr dataAsm = result[ModuleName]["gameData"];
            IntPtr dataPtr = game.FromAssemblyAddress(dataAsm + 0x1);
            IntPtr jumpPtr = game.FromRelativeAddress(dataAsm + 0xD);

            var scanner = new SignatureScanner(game, jumpPtr, 0x100);
            IntPtr dataOffset = scanner.Scan(new ScanTarget(0x51, "E8 ???????? C7 86 ???????? 0F 00 00 00 C7 86"));

            StaticDataOffset = dataPtr + game.Read<int>(dataOffset);

            logger.Log("Looking for version");
            while(true) {
                scanTask.token.ThrowIfCancellationRequested();

                if(new UnionStringPointer(game, StaticDataOffset).New == "none") {
                    if(new UnionStringPointer(game, StaticDataOffset + 0x50).New == "none") {
                        logger.Log("Version < 1.04");
                    } else {
                        logger.Log("Version >= 1.04");
                        StaticDataOffset += 0x18;
                    }
                    break;
                }
                System.Threading.Thread.Sleep(200);
            }

            Mode = new UnionStringPointer(game, StaticDataOffset + 0x68);
            _ = Mode.New;

            Level = new UnionStringPointer(game, StaticDataOffset + 0x98);
            _ = Level.New;

            Playtime = ptrFactory.Make<double>(StaticDataOffset + 0x470);

            IntPtr savePtr = game.FromAssemblyAddress(result[ModuleName]["saveData"]);
            savePtr += game.Read<int>(result[ModuleName]["saveDataOffset"]);

            var saveData = ptrFactory.Make<IntPtr>(savePtr);
            Activatables = ptrFactory.Make<IntPtr>(saveData, 0x4);
            Chapters = ptrFactory.Make<IntPtr>(saveData, 0x40);

            logger.Log(ptrFactory);

            scanTask = null;
        }

        public override bool Update() => base.Update() && scanTask == null;

        public void OnStart() {
            activatablesDone.Clear();
            activatablesCount = 0;

            chaptersDone.Clear();
            chaptersCount = 0;
        }

        public bool IsFlagNonZero(int offset) {
            return FlagValue(offset) != 0;
        }
        public double FlagValue(int offset) {
            return game.Read<double>(StaticDataOffset + offset);
        }

        public IEnumerable<string> NewActivatables() {
            int count = activatablesCount;
            activatablesCount = game.Read<int>(Activatables.New + SaveDictOffset);
            if(count < activatablesCount) {
                return NewInLinkedList(Activatables.New + SaveDictOffset, activatablesCount, activatablesDone);
            }
            return Array.Empty<string>();
        }

        public IEnumerable<string> NewChapters() {
            int count = chaptersCount;
            chaptersCount = game.Read<int>(Chapters.New + SaveDictOffset);
            if(count < chaptersCount) {
                return NewInLinkedList(Chapters.New + SaveDictOffset, chaptersCount, chaptersDone);
            }
            return Array.Empty<string>();
        }

        private IEnumerable<string> NewInLinkedList(IntPtr pointer, int count, HashSet<string> done) {
            var newNames = new List<string>();
            if(count > 1024) {
                // Probably garbage on game closing
                return newNames;
            }
            int length = game.Read<int>(pointer + 0x8);
            IntPtr arrayPtr = game.Read<IntPtr>(pointer + 0xC);
            IntPtr nodePtr = game.Read<IntPtr>(arrayPtr + length * 4);
            for(int i = 0; i < count; i++) {
                string name = game.Read<UnionString>(nodePtr + 0x8).Value(game);
                if(done.Add(name)) {
                    newNames.Add(name);
                }
                nodePtr = game.Read<IntPtr>(nodePtr);
            }
            return newNames;
        }

        //Can't use StructLayout + FieldOffset as we need an unmanaged object
        private unsafe struct UnionString {
            private fixed byte pointerUnionString[16];
#pragma warning disable 0649
            private readonly int size;
            private readonly int capacity;
#pragma warning restore 0649

            public unsafe string Value(ProcessWrapper wrapper) {
                fixed(byte* p = pointerUnionString) {
                    if(capacity > 0x0F) {
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