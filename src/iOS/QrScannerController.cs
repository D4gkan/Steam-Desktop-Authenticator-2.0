using AVFoundation;
using CoreFoundation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace Sda.Mobile.iOS;

public sealed class QrScannerController : UIViewController
{
    private readonly TaskCompletionSource<string?> _completion;
    private readonly AVCaptureSession _session = new();
    private readonly DispatchQueue _captureQueue = new("sda.qr.capture");
    private AVCaptureVideoPreviewLayer? _preview;
    private MetadataDelegate? _metadataDelegate;
    private bool _finished;
    private bool _cameraUnavailable;

    public QrScannerController(TaskCompletionSource<string?> completion)
    {
        _completion = completion;
        ModalPresentationStyle = UIModalPresentationStyle.FullScreen;
        ModalInPresentation = true;
    }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.Black;
        var camera = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
        if (camera == null) { _cameraUnavailable = true; return; }
        var input = AVCaptureDeviceInput.FromDevice(camera, out var error);
        if (input == null || error != null || !_session.CanAddInput(input))
        {
            error?.Dispose();
            _cameraUnavailable = true;
            return;
        }
        _session.AddInput(input);
        var output = new AVCaptureMetadataOutput();
        if (!_session.CanAddOutput(output)) { _cameraUnavailable = true; return; }
        _session.AddOutput(output);
        _metadataDelegate = new MetadataDelegate(value => Finish(value));
        output.SetDelegate(_metadataDelegate, DispatchQueue.MainQueue);
        output.MetadataObjectTypes = AVMetadataObjectType.QRCode;
        _preview = new AVCaptureVideoPreviewLayer(_session) { VideoGravity = AVLayerVideoGravity.ResizeAspectFill };
        View.Layer.AddSublayer(_preview);
        var cancel = new UIButton(UIButtonType.System) { Frame = new CGRect(20, 60, 100, 48) };
        cancel.SetTitle("Cancel", UIControlState.Normal);
        cancel.SetTitleColor(UIColor.White, UIControlState.Normal);
        cancel.BackgroundColor = UIColor.FromWhiteAlpha(0, 0.65f);
        cancel.Layer.CornerRadius = 10;
        cancel.TouchUpInside += (_, _) => Cancel();
        View.AddSubview(cancel);
        var hint = new UILabel(new CGRect(20, 130, View.Bounds.Width - 40, 70))
        {
            Text = "Scan the account QR shown by SDA Desktop", TextColor = UIColor.White,
            Lines = 0, TextAlignment = UITextAlignment.Center,
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth
        };
        View.AddSubview(hint);
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        if (_preview != null) _preview.Frame = View!.Bounds;
    }

    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        // Dismiss a failed scanner only after its presentation has completed.
        if (_cameraUnavailable) { Fail(); return; }
        if (!_finished) _captureQueue.DispatchAsync(() => _session.StartRunning());
    }

    public void Cancel() => Finish(null);

    private void Fail()
    {
        if (_finished) return;
        _finished = true;
        InvokeOnMainThread(async () =>
        {
            await DismissViewControllerAsync(true);
            _completion.TrySetException(new InvalidOperationException("The camera could not start. Import an account file instead."));
        });
    }

    private void Finish(string? value)
    {
        if (_finished) return;
        _finished = true;
        _captureQueue.DispatchAsync(() =>
        {
            _session.StopRunning();
            InvokeOnMainThread(async () =>
            {
                await DismissViewControllerAsync(true);
                _completion.TrySetResult(value);
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _preview?.Dispose();
            _metadataDelegate?.Dispose();
            _session.Dispose();
            _captureQueue.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class MetadataDelegate : AVCaptureMetadataOutputObjectsDelegate
    {
        private readonly Action<string> _found;
        public MetadataDelegate(Action<string> found) => _found = found;
        public override void DidOutputMetadataObjects(AVCaptureMetadataOutput captureOutput, AVMetadataObject[] metadataObjects, AVCaptureConnection connection)
        {
            var code = metadataObjects.OfType<AVMetadataMachineReadableCodeObject>().FirstOrDefault()?.StringValue;
            if (!string.IsNullOrEmpty(code)) _found(code);
        }
    }
}
