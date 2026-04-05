using CkCommons;
using CkCommons.HybridSaver;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using FFXIVClientStructs.FFXIV.Client.UI;
using NAudio.Wave;
using Sundouleia.Gui.Components;
using Sundouleia.Services;
using Sundouleia.Services.Configs;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Permissions;

namespace Sundouleia.PlayerClient;

public class ChatStorage
{
    // News (Future WIP)
    public bool NewsInChat { get; set; } = true;

    // Radar Spesific
    public bool RadarChat { get; set; } = true;
    public RadarChatFlags ChatFlags { get; set; } = RadarChatFlags.AllowPairRequests | RadarChatFlags.UseDisplayName;
    public bool RadarInChatbox { get; set; } = true;
    public XivChatType RadarChatType { get; set; } = XivChatType.Say;
    public uint RadarPrefixColor { get; set; } = 0; // 0 Implies Default, may set this later as a const.
    public uint RadarPrefixGlow { get; set; } = 0;

    // Client spesific
    public XivChatType DMChatType { get; set; } = XivChatType.Shout;
    public uint DMPrefixColor { get; set; } = 0; // 0 Implies Default, may set this later as a const.
    public uint DMPrefixGlow { get; set; } = 0;
    public bool AllowDMs { get; set; } = true;
    public bool Timestamps { get; set; } = true;
    public bool ShowEmotes { get; set; } = true;
    public bool LargerText { get; set; } = false;
    public float TextScale { get; set; } = 1.0f; //  Between 0.5x and 1.5x

    // Notifiers for mentions
    public bool MentionHighlights { get; set; } = true;
    public bool UnreadBubble { get; set; } = true;
    public AlertKind MentionPingKind { get; set; } = AlertKind.Bubble;
    public string PingCustomPath { get; set; } = string.Empty;
    public float PingVolume { get; set; } = 0.5f;
    public Sounds MentionPingGameSound { get; set; } = Sounds.Sound02;
    public bool PingsUseCustomSound { get; set; } = false;

    // Style
    public float WindowOpacity { get; set; } = 0.95f;
    public float UnfocusedWindowOpacity { get; set; } = 0.6f;

    // Rules
    public bool ShowInUIHide { get; set; } = false;
    public bool ShowInCutscene { get; set; } = false;
    public bool ShowInGroupPose { get; set; } = false;

    // Cached data
    public List<ChatCache> CachedChats { get; set; } = [];
}

public class ChatCache
{
    public string ChatId { get; set; } = string.Empty;
    public uint LabelColor { get; set; } = 0xFFFFFFFF;
    public uint BgColor { get; set; } = 0x66222222;
}

public class ChatConfig : IHybridSavable, IDisposable
{
    private readonly ILogger<ChatConfig> _logger;
    private readonly HybridSaveService _saver;

    // Cached items for custom alert configuration.
    private AudioFileReader? _audioFile;
    private WaveOutEvent? _audioEvent;

    // Hybrid Savable stuff
    [JsonIgnore] public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    [JsonIgnore] public HybridSaveType SaveType => HybridSaveType.Json;
    public int ConfigVersion => 0;
    public string GetFileName(ConfigFileProvider files, out bool upa) => (upa = false, files.ChatConfig).Item2;
    public void WriteToStream(StreamWriter writer) => throw new NotImplementedException();
    public string JsonSerialize()
    {
        return new JObject()
        {
            ["Version"] = ConfigVersion,
            ["Config"] = JObject.FromObject(Current),
        }.ToString(Formatting.Indented);
    }

    public ChatConfig(ILogger<ChatConfig> logger, HybridSaveService saver)
    {
        _logger = logger;
        _saver = saver;
        Load();
    }

    public void Dispose()
        => DisposeAudio();

    private void DisposeAudio()
    {
        _audioFile?.Dispose();
        _audioFile = null;
        _audioEvent?.Dispose();
        _audioEvent = null;
    }

    public void Save()
        => _saver.Save(this);
    
    public void Load()
    {
        var file = _saver.FileNames.ChatConfig;
        _logger.LogInformation($"Loading in Chat Config: {file}");
        if (!File.Exists(file))
        {
            _logger.LogWarning($"ChatConfig file not found: {file}");
            _saver.Save(this);
            return;
        }

        var jsonText = File.ReadAllText(file);
        var jObject = JObject.Parse(jsonText);
        var version = jObject["Version"]?.Value<int>() ?? 0;

            // Load instance configuration
        Current = jObject["Config"]?.ToObject<ChatStorage>() ?? new ChatStorage();
        Save();
    }

    public ChatStorage Current { get; private set; } = new();
    
    // Audio Helpers
    public bool IsPingSoundReady()
        => !Current.PingsUseCustomSound || IsCustomPingReady();

    public bool PlayPingGameSound()
    {
        if (!Current.MentionPingKind.HasAny(AlertKind.Audio))
            return false;

        if (Current.PingsUseCustomSound)
            return PlayCustomPing();

        UIGlobals.PlaySoundEffect((uint)Current.MentionPingGameSound);
        return true;
    }

    public void UpdatePingAudio()
    {
        // Dispose the audio if no longer valid.
        if (!(Current.MentionPingKind.HasAny(AlertKind.Audio) && Current.PingsUseCustomSound))
        {
            DisposeAudio();
            return;
        }

        try
        {
            // If the audio file name is no longer the chosen sound path, dispose of it.
            if (_audioFile?.FileName != Current.PingCustomPath)
                DisposeAudio();

            // Recreate the audio with the requested path and volume.
            _audioFile = new AudioFileReader(Current.PingCustomPath) { Volume = Current.PingVolume };
            _audioEvent = new WaveOutEvent();
            _audioEvent.Init(_audioFile);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error setting up alert sound: {ex}");
            DisposeAudio();
        }
    }

    private bool IsCustomPingReady()
        => _audioFile != null && _audioEvent != null;

    private bool PlayCustomPing()
    {
        if (_audioFile is null || _audioEvent is null)
            return false;

        _audioEvent!.Stop();
        _audioFile!.Position = 0;
        _audioEvent!.Play();
        return true;
    }
}
