using CkCommons.Gui;
using CkCommons.Helpers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures.TextureWraps; // This is discouraged, try and look into better way to do it later.
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using Sundouleia.Utils;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;

namespace Sundouleia.Gui.Profiles;

// Seems to be a little sensitive since 7.3... look into why, might be something with
// SixLabors ImageSharp
public class ProfileAvatarEditor : WindowMediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly UiFileDialogService _fileDialog;
    private readonly ProfileService _service;
    private readonly CosmeticService _cosmetics;

    public ProfileAvatarEditor(ILogger<ProfileAvatarEditor> logger, SundouleiaMediator mediator,
        MainHub hub, UiFileDialogService fileDialog, ProfileService service) 
        : base(logger, mediator, "Avatar Editor###KP_PFP_UI")
    {
        _hub = hub;
        _fileDialog = fileDialog;
        _service = service;

        Flags = WFlags.NoResize | WFlags.NoCollapse | WFlags.NoScrollbar;
        IsOpen = false;
        this.SetBoundaries(new(768, 600));
        this.PinningClickthroughFalse();

        Mediator.Subscribe<DisconnectedMessage>(this, (_) => IsOpen = false);
    }

    private bool _showFileDialogError = false;

    // Store the original image data of our imported file.
    private byte[] _uploadedImageData;
    // Determine if we are using a compressed image.
    private bool _useCompressedImage = false; // Default to using compressed image
    private byte[] _compressedImageData;

    // hold a temporary image data of the cropped image area without affecting the original or compressed image.
    private byte[] _scopedData = null!;
    private byte[] _croppedImageData;
    private IDalamudTextureWrap? _croppedImageToShow;

    // the other values for movement, rotation, and scaling.
    private float _cropX = 0.5f; // Center by default
    private float _cropY = 0.5f; // Center by default
    private float _rotationAngle = 0.0f; // Rotation angle in degrees
    private float _zoomFactor = 1.0f; // Zoom factor, 1.0 means no zoom
    private float _minZoomFactor = 1.0f;
    private float _maxZoomFactor = 3.0f;

    // Store file size references for both debug metrics and to show the user how optimized their images is.
    public string OriginalFileSize { get; private set; } = string.Empty;
    public string ScaledFileSize { get; private set; } = string.Empty;
    public string CroppedFileSize { get; private set; } = string.Empty;

    protected override void PreDrawInternal() { }
    protected override void PostDrawInternal() { }
    protected override void DrawInternal()
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var wdl = ImGui.GetWindowDrawList();
        // grab our profile.
        var profile = _service.GetProfile(MainHub.OwnUserData);
        if (profile.Info.Flagged)
        {
            CkGui.ColorTextWrapped("Cannot Edit Profiles right now.", ImGuiColors.DalamudRed);
            return;
        }

        // Draw out the current image.
        var curAvatar = profile.GetAvatarOrDefault();
        ImGui.Image(curAvatar.Handle, ImGuiHelpers.ScaledVector2(curAvatar.Width, curAvatar.Height));

        ImGui.SameLine();
        var pos = ImGui.GetCursorScreenPos(); 
        wdl.AddDalamudImageRounded(curAvatar, pos, curAvatar.Size, 128f);
        // SameLine() the spacing equal to the distance of the image.
        ImGuiHelpers.ScaledRelativeSameLine(256, spacing);

        // we need here to draw the group for content.
        using (ImRaii.Group())
        {
            CkGui.FontText("Current Image", UiFontService.UidFont);
            ImGui.Separator();
            CkGui.ColorText("Square Image Preview:", ImGuiColors.ParsedGold);
            CkGui.TextWrapped("Meant to display the original display of the stored image data.");
            ImGui.Spacing();
            CkGui.ColorText("Rounded Image Preview:", ImGuiColors.ParsedGold);
            CkGui.TextWrapped("This is what's seen in the account page, and inside of Profiles");
            ImGui.Spacing();

            var width = CkGui.IconTextButtonSize(FAI.Trash, "Clear uploaded profile picture");
            // move down to the newline and draw the buttons for adding and removing images
            if (CkGui.IconTextButton(FAI.FileUpload, "Upload new profile picture", width))
                HandleFileDialog();
            CkGui.AttachToolTip("Select and upload a new profile picture");

            // let them clean their image too if they desire.
            if (CkGui.IconTextButton(FAI.Trash, "Clear uploaded profile picture", width, disabled: !KeyMonitor.ShiftPressed()))
            {
                _uploadedImageData = null!;
                _croppedImageData = null!;
                _croppedImageToShow = null;
                _useCompressedImage = false;
                UiService.SetUITask(async () => await _hub.UserUpdateProfilePicture(new(string.Empty)));
            }
            CkGui.AttachToolTip("Clear your currently uploaded profile picture--SEP--Must be holding SHIFT to clear.");

            // show file dialog error if we had one.
            if (_showFileDialogError)
                CkGui.ColorTextWrapped("The profile picture must be a PNG file", ImGuiColors.DalamudRed);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // draw out the editor settings.
        if (_uploadedImageData != null)
            DrawNewProfileDisplay(profile);
    }

    private void HandleFileDialog()
    {
        _fileDialog.OpenSingleFilePicker("Select new Profile picture", ".png", (success, file) =>
        {
            if (!success)
            {
                _logger.LogWarning("Failed to open file dialog.");
                return;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("Attempting to upload new profile picture.");
                    var fileContent = File.ReadAllBytes(file);
                    using (MemoryStream ms = new(fileContent))
                    {
                        var format = await Image.DetectFormatAsync(ms).ConfigureAwait(false);
                        if (!format.FileExtensions.Contains("png", StringComparer.OrdinalIgnoreCase))
                        {
                            _showFileDialogError = true;
                            return;
                        }
                        // store the original file size
                        OriginalFileSize = $"{fileContent.Length / 1024.0:F2} KB";
                        _uploadedImageData = ms.ToArray();
                    }

                    // Load and process the image
                    using (var image = Image.Load<Rgba32>(fileContent))
                    {
                        // Calculate the scale factor to ensure the smallest dimension is 256 pixels
                        var scale = 256f / Math.Min(image.Width, image.Height);
                        var scaledSize = new Size((int)(image.Width * scale), (int)(image.Height * scale));

                        InitializeZoomFactors(image.Width, image.Height);

                        // Resize the image while maintaining the aspect ratio
                        var resizedImage = image.Clone(ctx => ctx.Resize(new ResizeOptions
                        {
                            Size = scaledSize,
                            Mode = ResizeMode.Max
                        }));

                        // Convert the processed image to byte array
                        using (var ms = new MemoryStream())
                        {
                            resizedImage.SaveAsPng(ms);
                            _scopedData = ms.ToArray();

                            // Initialize cropping parameters
                            _cropX = 0.5f;
                            _cropY = 0.5f;
                            ScaledFileSize = $"{_scopedData.Length / 1024.0:F2} KB";

                            // Update the preview image
                            UpdateCroppedImagePreview();
                        }
                    }

                    _showFileDialogError = false;
                }
                catch (Bagagwa ex)
                {
                    _logger.LogError(ex, "Failed to upload new profile picture.");
                }
            });
        });
    }

    private void DrawNewProfileDisplay(Profile profile)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        if (_uploadedImageData != null)
        {
            // display the respective info on the line below.
            if (_croppedImageData != null)
            {
                // ensure the wrap for the data is not yet null
                if (_croppedImageToShow != null)
                {
                    ImGui.Image(_croppedImageToShow.Handle, ImGuiHelpers.ScaledVector2(_croppedImageToShow.Width, _croppedImageToShow.Height), Vector2.Zero, Vector2.One, ImGuiColors.DalamudWhite, ImGuiColors.DalamudWhite);

                    ImGuiHelpers.ScaledRelativeSameLine(256, spacing);
                    var currentPosition = ImGui.GetCursorPos();
                    var pos = ImGui.GetCursorScreenPos();
                    ImGui.GetWindowDrawList().AddImageRounded(_croppedImageToShow.Handle, pos, pos + _croppedImageToShow.Size, Vector2.Zero, Vector2.One, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), 128f);
                    ImGui.SetCursorPos(new Vector2(currentPosition.X, currentPosition.Y + _croppedImageToShow.Height));
                }
            }
        }
        // draw the slider and the update buttons
        ImGuiHelpers.ScaledRelativeSameLine(256, spacing);
        using (ImRaii.Group())
        {
            CkGui.FontText("Image Editor", UiFontService.UidFont);
            ImGui.Separator();
            CkGui.ColorText("Adjustments may crash your game atm! (WIP)", ImGuiColors.DalamudRed);
            if (_croppedImageData != null)
            {
                var cropXref = _cropX;
                ImGui.SetNextItemWidth(150f);
                if (ImGui.SliderFloat("Width", ref _cropX, 0.0f, 1.0f, "%.2f"))
                    if (cropXref != _cropX)
                        UpdateCroppedImagePreview();

                var cropYref = _cropY;
                ImGui.SetNextItemWidth(150f);
                if (ImGui.SliderFloat("Height", ref _cropY, 0.0f, 1.0f, "%.2f"))
                    if (cropYref != _cropY)
                        UpdateCroppedImagePreview();

                // Add rotation slider
                var rotationRef = _rotationAngle;
                ImGui.SetNextItemWidth(150f);
                if (ImGui.SliderFloat("Rotation", ref _rotationAngle, 0.0f, 360.0f, "%.2f"))
                    if (rotationRef != _rotationAngle)
                        UpdateCroppedImagePreview();
                CkGui.AttachToolTip("DOES NOT WORK YET!");

                // Add zoom slider
                var zoomRef = _zoomFactor;
                ImGui.SetNextItemWidth(150f);
                if (ImGui.SliderFloat("Zoom", ref _zoomFactor, _minZoomFactor, _maxZoomFactor, "%.2f"))
                    if (zoomRef != _zoomFactor)
                        UpdateCroppedImagePreview();
            }

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Original Image Size: " + OriginalFileSize);

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Scaled Image Size: " + ScaledFileSize);

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Cropped Image Size: " + CroppedFileSize);

            // draw the compress & upload.
            if (CkGui.IconTextButton(FAI.Compress, "Compress"))
                CompressImage();
            CkGui.AttachToolTip("Shrinks the image to a 512x512 ratio for better performance");

            ImGui.SameLine();

            if (CkGui.IconTextButton(FAI.Upload, "Upload to Server", disabled: _croppedImageData is null))
                UploadToServer(profile);
        }
    }

    private void UploadToServer(Profile profile)
    {
        // grab the _croppedImageData and upload it to the server.
        if (_croppedImageData is null)
            return;

        // update the cropped image data to the 256x256 standard it should be.
        using (var image = Image.Load<Rgba32>(_uploadedImageData))
        {
            // Resize the zoomed area to 512x512
            var resizedImage = image.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(256, 256),
                Mode = ResizeMode.Max
            }));


            // Convert the processed image to byte array
            using (var ms = new MemoryStream())
            {
                resizedImage.SaveAsPng(ms);
                _compressedImageData = ms.ToArray();
                _logger.LogTrace("New Image File Size: " + _compressedImageData.Length / 1024.0 + " KB");
                _logger.LogDebug($"Sending Image to server with: {resizedImage.Width}x{resizedImage.Height} [Original: {image.Width}x{image.Height}]");
            }
        }
        try
        {
            UiService.SetUITask(async () =>
            {
                if (await _hub.UserUpdateProfilePicture(new(Convert.ToBase64String(_croppedImageData))) is { } res)
                {
                    if (res.ErrorCode is SundouleiaApiEc.Success)
                    {
                        _logger.LogInformation("Image Sent to server successfully.");
                        Mediator.Publish(new ClearProfileDataMessage(MainHub.OwnUserData));
                    }
                    else
                    {
                        _logger.LogError($"Failed to send image to server: {res.ErrorCode}");
                    }
                }
            });
        }
        catch (Bagagwa ex)
        {
            _logger.LogError(ex, "Failed to send image to server.");
        }
    }

    private void InitializeZoomFactors(int width, int height)
    {
        var lesserDimension = Math.Min(width, height);
        _minZoomFactor = 1.0f;
        _maxZoomFactor = lesserDimension / 256.0f; // Ensure the minimum zoomed area is 256x256
    }

    public void CompressImage()
    {
        if (_uploadedImageData == null) return;

        using (var image = Image.Load<Rgba32>(_uploadedImageData))
        {
            // Calculate the lesser dimension of the original image
            var lesserDimension = Math.Min(image.Width, image.Height);

            // Calculate the cropping rectangle to make the image square
            var cropRectangle = new Rectangle(0, 0, lesserDimension, lesserDimension);
            cropRectangle.X = (image.Width - lesserDimension) / 2;
            cropRectangle.Y = (image.Height - lesserDimension) / 2;

            // Crop the image to the lesser dimension
            var croppedImage = image.Clone(ctx => ctx.Crop(cropRectangle));

            var desiredSize = new Size(512, 512);

            // Resize the zoomed area to 512x512
            var resizedImage = croppedImage.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = desiredSize,
                Mode = ResizeMode.Max
            }));


            // Convert the processed image to byte array
            using (var ms = new MemoryStream())
            {
                resizedImage.SaveAsPng(ms);
                _compressedImageData = ms.ToArray();
                _useCompressedImage = true;
                // _logger.LogDebug($"New Image width and height is: {resizedImage.Width}x{resizedImage.Height} from {croppedImage.Width}x{croppedImage.Height}");
                CroppedFileSize = $"{_croppedImageData.Length / 1024.0:F2} KB";
                InitializeZoomFactors(resizedImage.Width, resizedImage.Height);
            }
        }
    }

    private void UpdateCroppedImagePreview()
    {
        // Ensure the image data is not 
        if (_uploadedImageData == null) return;


        using (var image = Image.Load<Rgba32>(_useCompressedImage ? _compressedImageData : _uploadedImageData))
        {
            var desiredSize = new Size(256, 256);
            // Calculate the lesser dimension of the original image
            var lesserDimension = Math.Min(image.Width, image.Height);

            // Calculate the size of the zoomed area based on the zoom factor
            var zoomedWidth = (int)(lesserDimension / _zoomFactor);
            var zoomedHeight = (int)(lesserDimension / _zoomFactor);

            // Ensure the zoomed area is at least 256x256
            zoomedWidth = Math.Max(zoomedWidth, 256);
            zoomedHeight = Math.Max(zoomedHeight, 256);

            // Ensure the zoomed area does not exceed the lesser dimension of the original image
            zoomedWidth = Math.Min(zoomedWidth, lesserDimension);
            zoomedHeight = Math.Min(zoomedHeight, lesserDimension);

            // Calculate the cropping rectangle based on the user's alignment selection
            var cropRectangle = new Rectangle(0, 0, zoomedWidth, zoomedHeight);
            cropRectangle.X = Math.Max(0, Math.Min((int)((image.Width - zoomedWidth) * _cropX), image.Width - zoomedWidth));
            cropRectangle.Y = Math.Max(0, Math.Min((int)((image.Height - zoomedHeight) * _cropY), image.Height - zoomedHeight));

            // Ensure the crop rectangle is within the image bounds
            cropRectangle.Width = Math.Min(cropRectangle.Width, image.Width - cropRectangle.X);
            cropRectangle.Height = Math.Min(cropRectangle.Height, image.Height - cropRectangle.Y);

            var zoomedImage = image.Clone(ctx => ctx.Crop(cropRectangle));

            // Resize the zoomed area to 256x256
            var croppedImage = zoomedImage.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = desiredSize,
                Mode = ResizeMode.Max
            }));


            // Convert the processed image to byte array
            using (var ms = new MemoryStream())
            {
                croppedImage.SaveAsPng(ms);
                _croppedImageData = ms.ToArray();

                // Load the cropped image for preview
                _croppedImageToShow = Svc.Texture.CreateFromImageAsync(_croppedImageData).Result;
                CroppedFileSize = $"{_croppedImageData.Length / 1024.0:F2} KB";
                // _logger.LogInformation($"Cropped image to {cropRectangle}");
            }
        }
    }

}
