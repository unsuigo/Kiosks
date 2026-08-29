using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

using Mediapipe;
using Mediapipe.Tasks.Vision.FaceDetector;
using Mediapipe.Unity;
using Mediapipe.Unity.Sample;
using Mediapipe.Unity.Sample.FaceDetection;

using FaceDetectionResult = Mediapipe.Tasks.Components.Containers.DetectionResult;
using Experimental = Mediapipe.Unity.Experimental;
using Tasks = Mediapipe.Tasks;


namespace Kiosk.ComputerVision.FaceDetection
{
  public class KioskFaceDetectorRunner : VisionTaskApiRunner<FaceDetector>, IFaceDetector
  {
    [SerializeField] private DetectionResultAnnotationController _detectionResultAnnotationController;
    [SerializeField] private float faceLostDelay = 2f;
    private float _lastFaceSeenTime;

    private Experimental.TextureFramePool _textureFramePool;

    public readonly FaceDetectionConfig config = new FaceDetectionConfig();
    
    public Texture CurrentCameraTexture
    {
      get
      {
        var imageSource = ImageSourceProvider.ImageSource;

        if (imageSource == null || !imageSource.isPrepared)
          return null;

        return imageSource.GetCurrentTexture();
      }
    }
    
    public bool HasFace { get; private set; }
    
    public event Action FaceDetected;
    public event Action FaceLost;
    

    private volatile bool _latestHasFace;

    public override void Stop()
    {
      base.Stop();
      _textureFramePool?.Dispose();
      _textureFramePool = null;
    }

    protected override IEnumerator Run()
    {
      Debug.Log($"Delegate = {config.Delegate}");
      Debug.Log($"Image Read Mode = {config.ImageReadMode}");
      Debug.Log($"Model = {config.ModelName}");
      Debug.Log($"Running Mode = {config.RunningMode}");
      Debug.Log($"MinDetectionConfidence = {config.MinDetectionConfidence}");
      Debug.Log($"MinSuppressionThreshold = {config.MinSuppressionThreshold}");
      Debug.Log($"NumFaces = {config.NumFaces}");

      yield return AssetLoader.PrepareAssetAsync(config.ModelPath);

      var options = config.GetFaceDetectorOptions(config.RunningMode == Tasks.Vision.Core.RunningMode.LIVE_STREAM ? OnFaceDetectionsOutput : null);
      taskApi = FaceDetector.CreateFromOptions(options, GpuManager.GpuResources);
      var imageSource = ImageSourceProvider.ImageSource;

      yield return imageSource.Play();

      if (!imageSource.isPrepared)
      {
        Debug.LogError("Failed to start ImageSource, exiting...");
        yield break;
      }

      // Use RGBA32 as the input format.
      // TODO: When using GpuBuffer, MediaPipe assumes that the input format is BGRA, so maybe the following code needs to be fixed.
      _textureFramePool = new Experimental.TextureFramePool(imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10);

      // NOTE: The screen will be resized later, keeping the aspect ratio.
      screen.Initialize(imageSource);

      SetupAnnotationController(_detectionResultAnnotationController, imageSource);

      var transformationOptions = imageSource.GetTransformationOptions();
      var flipHorizontally = transformationOptions.flipHorizontally;
      var flipVertically = transformationOptions.flipVertically;
      var imageProcessingOptions = new Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: (int)transformationOptions.rotationAngle);

      AsyncGPUReadbackRequest req = default;
      var waitUntilReqDone = new WaitUntil(() => req.done);
      var waitForEndOfFrame = new WaitForEndOfFrame();
      var result = FaceDetectionResult.Alloc(options.numFaces);

      // NOTE: we can share the GL context of the render thread with MediaPipe (for now, only on Android)
      var canUseGpuImage = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 && GpuManager.GpuResources != null;
      using var glContext = canUseGpuImage ? GpuManager.GetGlContext() : null;

      while (true)
      {
        if (isPaused)
        {
          yield return new WaitWhile(() => isPaused);
        }

        if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
        {
          yield return null;
          continue;
        }

        // Build the input Image
        Image image;
        switch (config.ImageReadMode)
        {
          case ImageReadMode.GPU:
            if (!canUseGpuImage)
            {
              throw new System.Exception("ImageReadMode.GPU is not supported");
            }
            textureFrame.ReadTextureOnGPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            image = textureFrame.BuildGPUImage(glContext);
            // TODO: Currently we wait here for one frame to make sure the texture is fully copied to the TextureFrame before sending it to MediaPipe.
            // This usually works but is not guaranteed. Find a proper way to do this. See: https://github.com/homuler/MediaPipeUnityPlugin/pull/1311
            yield return waitForEndOfFrame;
            break;
          case ImageReadMode.CPU:
            yield return waitForEndOfFrame;
            textureFrame.ReadTextureOnCPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            image = textureFrame.BuildCPUImage();
            textureFrame.Release();
            break;
          case ImageReadMode.CPUAsync:
          default:
            req = textureFrame.ReadTextureAsync(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            yield return waitUntilReqDone;

            if (req.hasError)
            {
              Debug.LogWarning($"Failed to read texture from the image source");
              continue;
            }
            image = textureFrame.BuildCPUImage();
            textureFrame.Release();
            break;
        }

        switch (taskApi.runningMode)
        {
          case Tasks.Vision.Core.RunningMode.IMAGE:
            if (taskApi.TryDetect(image, imageProcessingOptions, ref result))
            {
              _detectionResultAnnotationController.DrawNow(result);
            }
            else
            {
              // clear the annotation
              _detectionResultAnnotationController.DrawNow(default);
            }
            break;
          case Tasks.Vision.Core.RunningMode.VIDEO:
            if (taskApi.TryDetectForVideo(image, GetCurrentTimestampMillisec(), imageProcessingOptions, ref result))
            {
              _detectionResultAnnotationController.DrawNow(result);
            }
            else
            {
              // clear the annotation
              _detectionResultAnnotationController.DrawNow(default);
            }
            break;
          case Tasks.Vision.Core.RunningMode.LIVE_STREAM:
            taskApi.DetectAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
            break;
        }
      }
    }
    
    private void Update()
    {
      if (_latestHasFace)
      {
        _lastFaceSeenTime = Time.unscaledTime;

        if (!HasFace)
        {
          HasFace = true;

          Debug.Log("[Kiosk] Face detected");
          FaceDetected?.Invoke();
        }

        return;
      }

      if (!HasFace)
        return;

      if (Time.unscaledTime - _lastFaceSeenTime < faceLostDelay)
        return;

      HasFace = false;

      Debug.Log("[Kiosk] Face lost");
      FaceLost?.Invoke();
    }

    private void OnFaceDetectionsOutput(FaceDetectionResult result, Image image, long timestamp)
    {
      _latestHasFace = result.detections != null && result.detections.Count > 0;
      _detectionResultAnnotationController.DrawLater(result);
    }
   
  }
}


