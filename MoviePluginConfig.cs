using FrostySdk.Attributes;
using FrostySdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FrostySdk.IO;
using Frosty.Core;
using Frosty.Core.Controls.Editors;

namespace MovieTexture2AssetEditorPlugin
{
    public class MoviePluginConfig : OptionsExtension
    {
        [Category("General")]
        [Description("The default volume level to use 0=mute, 100=0db")]
        [EbxFieldMeta(EbxFieldType.Int32)]
        public int DefaultVolume { get; set; } = 100;

        [Category("General")]
        [Description("If true, a video will automatically start playing after import.")]
        [Editor(typeof(FrostyBooleanEditor))]
        [EbxFieldMeta(EbxFieldType.Boolean)]
        public bool AutoplayOnImport { get; set; } = false;

        public static MoviePluginConfig GetConfig()
        {
            MoviePluginConfig config = new MoviePluginConfig();
            config.Load();
            return config;
        }

        public override void Load()
        {
            DefaultVolume = Config.Get("MovieTexture.DefaultVolume", 100);
            AutoplayOnImport = Config.Get("MovieTexture.AutoplayOnImport", false);
        }

        public override void Save() {
            Config.Add("MovieTexture.DefaultVolume", DefaultVolume);
            Config.Add("MovieTexture.AutoplayOnImport", AutoplayOnImport);
            Config.Save();
        }

        public override bool Validate()
        {
            return !(DefaultVolume < 0 || DefaultVolume > 100);
        }
    }
}
