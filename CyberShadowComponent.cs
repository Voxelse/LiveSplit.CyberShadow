using LiveSplit.Model;
using LiveSplit.UI.Components;
using System;
using System.ComponentModel;
using System.Windows.Forms;
using Voxif.AutoSplitter;
using Voxif.IO;

[assembly: ComponentFactory(typeof(Factory))]
namespace LiveSplit.CyberShadow {
    public partial class CyberShadowComponent : Voxif.AutoSplitter.Component {

        public enum EOption {
            [Description("Room Timer"), Type(typeof(OptionCheckBox))]
            RoomTimer
        }

        protected override OptionsInfo? OptionsSettings => new OptionsInfo(null, CreateControlsFromEnum<EOption>());
        protected override EGameTime GameTimeType => EGameTime.GameTime;
        protected override bool IsGameTimeDefault => false;

        private CyberShadowMemory memory;

        public CyberShadowComponent(LiveSplitState state) : base(state) {
#if DEBUG
            logger = new ConsoleLogger();
#else
            logger = new FileLogger("_" + Factory.ExAssembly.GetName().Name.Substring(10) + ".log");
#endif
            logger.StartLogger();
            
            memory = new CyberShadowMemory(logger);

            settings = new TreeSettings(state, StartSettings, ResetSettings, OptionsSettings);
            settings.OptionChanged += OptionChanged;
            
            remainingSplits = new RemainingDictionary(logger);
        }

        private void OptionChanged(Control sender, OptionEventArgs e) {
            switch(Enum.Parse(typeof(EOption), e.Name)) {
                case EOption.RoomTimer:
                    RoomTimer = e.State == 1;
                    break;
            }
        }

        public override void Dispose() {
            settings.OptionChanged -= OptionChanged;
            RoomTimer = false;
            memory.Dispose();
            memory = null;
            base.Dispose();
        }
    }
}