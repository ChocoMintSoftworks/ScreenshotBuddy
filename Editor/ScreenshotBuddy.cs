using System.Collections;
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace ChocoMintSoftworks.ScreenshotBuddy
{
  public class ScreenshotBuddy : EditorWindow
  {
    static Texture2D framingTexture;
    static bool overlayEnabled = false;
    public static int resWidth = 1920;
    public static int resHeight = 1080;
    public static int jpgQuality = 100;
    public static string[] formats = new string[] { "PNG", "JPG", "TGA" };
    public static int selectedFormat = 0;

    public static string[] availableMsaaSamples = new string[] { "Disabled", "2x", "4x", "8x" };
    public static int selectedMsaaSamples = 3;
    public static string userFilePrefix = "screenshot";

    public static bool enableTransparency = false;
    public static string[] superSampleOptions = new string[] { "1x", "2x", "3x", "4x" };
    public static int selectedSuperSample = 0;
    private static bool ppOriginalState = false;

    public static Color32 overlayColor = new Color32(255, 0, 0, 80);

    [MenuItem("Window/ScreenshotBuddy")]
    public static void ShowWindow()
    {
      GetWindow<ScreenshotBuddy>("ScreenshotBuddy");
    }

    ////// RENDER GUI HERE
    void OnGUI()
    {
      EditorStyles.label.wordWrap = true;
      GUILayout.Label("Screenshot Settings", EditorStyles.boldLabel);
      resWidth = EditorGUILayout.IntField("Width:", resWidth);
      resHeight = EditorGUILayout.IntField("Height:", resHeight);
      selectedFormat = EditorGUILayout.Popup("Format:", selectedFormat, formats);
      if (selectedFormat == 1)
      {
        GUILayout.BeginHorizontal();
        GUILayout.Label("JPG Quality:", EditorStyles.label);
        jpgQuality = EditorGUILayout.IntSlider(jpgQuality, 1, 100);
        GUILayout.EndHorizontal();

        if (jpgQuality < 20)
        {
          GUILayout.Label("Setting the quality below 20 results in barely usable screenshots, considering raising it again.", EditorStyles.label);
        }
      }

      selectedMsaaSamples = EditorGUILayout.Popup("MSAA:", selectedMsaaSamples, availableMsaaSamples);
      selectedSuperSample = EditorGUILayout.Popup("Supersample:", selectedSuperSample, superSampleOptions);
      enableTransparency = GUILayout.Toggle(enableTransparency, "Enable Transparency");
      if (enableTransparency)
      {
        GUILayout.Label("Notice: Enabling transparency disables post processing due to a unity bug.", EditorStyles.label);
      }
      GUILayout.BeginHorizontal();
      GUILayout.Label("Filename Prefix:", EditorStyles.label);
      userFilePrefix = GUILayout.TextField(userFilePrefix, 32);
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      if (GUILayout.Button("Take Screenshot", GUILayout.ExpandWidth(false)))
      {
        Screenshot();
      }
      if (GUILayout.Button("Show Screenshot Folder", GUILayout.ExpandWidth(false)))
      {
        OpenScreenshotFolder();
      }
      GUILayout.EndHorizontal();

      GUILayout.Label("Framing Overlay", EditorStyles.boldLabel);

      GUILayout.BeginHorizontal();
      if (GUILayout.Button("Enable", GUILayout.ExpandWidth(false)))
      {
        EnableOverlay();
      }

      if (GUILayout.Button("Disable", GUILayout.ExpandWidth(false)))
      {
        DisableOverlay();
      }

      if (GUILayout.Button("Refresh", GUILayout.ExpandWidth(false)))
      {
        GenerateOverlayTexture();
      }
      GUILayout.EndHorizontal();

      overlayColor = EditorGUILayout.ColorField("Overlay color: ", overlayColor);

    }

    static void OpenScreenshotFolder()
    {
      string path = @".\Screenshots\";
      if (!Directory.Exists(path))
      {
        Directory.CreateDirectory(path);
      }
      EditorUtility.RevealInFinder(path);
    }

    static void Screenshot()
    {
      bool screenshotTaken = CaptureEditorScreenshot(userFilePrefix);
    }

    public static bool CaptureEditorScreenshot(string filePrefix)
    {
      string screenshotFolder = Path.Combine(Application.dataPath, "../Screenshots/");
      screenshotFolder = Path.GetFullPath(screenshotFolder);

      if (!Directory.Exists(screenshotFolder))
      {
        Directory.CreateDirectory(screenshotFolder);
      }

      string screenshotFileName = screenshotFolder + filePrefix + "_" + DateTime.Now.ToString("yyMMdd_HHmmss");

      SceneView sw = SceneView.lastActiveSceneView;

      if (sw == null)
      {
        Debug.LogError("Unable to capture editor screenshot, no scene view found. Make sure you have a scene view window opened.");
        return false;
      }

      Camera cam = sw.camera;

      if (cam == null)
      {
        Debug.LogError("Unable to capture editor screenshot, no camera attached to current scene view.");
        return false;
      }

      if (cam.targetTexture != null)
      {
        // Avoid a memleak at all costs
        cam.targetTexture.Release();
        DestroyImmediate(cam.targetTexture);
      }

      var renderDesc = new RenderTextureDescriptor(resWidth * GetSuperSampleValue(), resHeight * GetSuperSampleValue(), RenderTextureFormat.ARGB32, 32);

      switch (selectedMsaaSamples)
      {
        case 0:
          renderDesc.msaaSamples = 1;
          break;
        case 1:
          renderDesc.msaaSamples = 2;
          break;
        case 2:
          renderDesc.msaaSamples = 4;
          break;
        case 3:
          renderDesc.msaaSamples = 8;
          break;
        default:
          renderDesc.msaaSamples = 1;
          break;
      }

      ppOriginalState = sw.sceneViewState.showImageEffects;

      if (enableTransparency)
      {
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(1, 1, 1, 0.0f);

        // Somehow, postprocessing disables transparency...
        sw.sceneViewState.showImageEffects = false;
      }

      cam.targetTexture = RenderTexture.GetTemporary(renderDesc);
      RenderTexture renderTexture = cam.targetTexture;

      cam.Render();

      if (renderTexture == null)
      {
        Debug.LogError("Unable to capture editor screenshot, couldn't create new render texture.");
        return false;
      }

      int width = renderTexture.width;
      int height = renderTexture.height;
      var outputTexture = new Texture2D(width, height, enableTransparency ? TextureFormat.ARGB32 : TextureFormat.RGB24, false);

      RenderTexture.active = renderTexture;

      outputTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);

      // Convert to srgb if using linear color space
      if (QualitySettings.activeColorSpace == ColorSpace.Linear)
      {
        Color[] pixels = outputTexture.GetPixels();
        for (int p = 0; p < pixels.Length; p++)
        {
          pixels[p] = pixels[p].gamma;
        }
        outputTexture.SetPixels(pixels);
      }
      outputTexture.Apply();

      // Dowsample image in half steps, otherwise the end result will be very aliased
      if (selectedSuperSample > 0)
      {
        //Debug.Log("Res pre downsample: " + resWidth * GetSuperSampleValue() + "x" + resHeight * GetSuperSampleValue());
        for (int i = selectedSuperSample; i > 1; i--)
        {
          //Debug.Log("Dowsampling to " + resWidth * i + "x" + resHeight * i);
          outputTexture = ResizeTexture(outputTexture, resWidth * i, resHeight * i);
        }

        //Debug.Log("Final downsample: " + resWidth + "x" + resHeight);
        outputTexture = ResizeTexture(outputTexture, resWidth, resHeight);
      }

      screenshotFileName = screenshotFileName + "." + formats[selectedFormat];
      FileStream screenshotFile = File.Create(screenshotFileName);

      if (!screenshotFile.CanWrite)
      {
        Debug.LogError("Unable to capture editor screenshot, Failed to open file for writing.");
        return false;
      }

      byte[] imageData = new byte[2];
      switch (selectedFormat)
      {
        case 0:
          imageData = outputTexture.EncodeToPNG();
          break;
        case 1:
          imageData = outputTexture.EncodeToJPG(jpgQuality);
          break;
        case 2:
          imageData = outputTexture.EncodeToTGA();
          break;
      }

      screenshotFile.Write(imageData, 0, imageData.Length);

      screenshotFile.Close();

      DestroyImmediate(outputTexture);
      RenderTexture.ReleaseTemporary(cam.targetTexture);

      sw.ShowNotification(new GUIContent("Screenshot written to file " + screenshotFileName));
      Debug.Log("Screenshot written to file " + screenshotFileName);
      sw.sceneViewState.showImageEffects = ppOriginalState;
      return true;
    }

    public static void EnableOverlay()
    {
      if (!overlayEnabled)
      {
        overlayEnabled = true;
        GenerateOverlayTexture();

        SceneView.duringSceneGui += OnSceneGUI;
      }
    }

    public static void DisableOverlay()
    {
      if (overlayEnabled)
      {
        DestroyImmediate(framingTexture);
        SceneView.duringSceneGui -= OnSceneGUI;
        overlayEnabled = false;
      }
    }

    public static void GenerateOverlayTexture()
    {
      framingTexture = new Texture2D(resWidth, resHeight, TextureFormat.RGBA32, false);

      Color32[] overlayColorArray = framingTexture.GetPixels32();

      for (int i = 0; i < overlayColorArray.Length; i++)
      {
        overlayColorArray[i] = overlayColor;
      }

      framingTexture.SetPixels32(overlayColorArray);
      framingTexture.Apply();
    }

    private static void OnSceneGUI(SceneView sceneview)
    {
      int viewWidth = GetWindow<SceneView>().camera.pixelWidth;
      int viewHeight = GetWindow<SceneView>().camera.pixelHeight;
      Handles.BeginGUI();
      GUI.DrawTexture(new Rect(0, 0, viewWidth, viewHeight), framingTexture, ScaleMode.ScaleToFit, true, 0.0f);
      Handles.EndGUI();
    }


    // Original source of the texture resizing code:
    // https://forum.unity.com/threads/how-to-resize-scale-down-texture-without-losing-quality.976965/
    public static Texture2D RenderMaterial(ref Material material, Vector2Int resolution)
    {
      RenderTexture renderTexture = RenderTexture.GetTemporary(resolution.x, resolution.y);
      Graphics.Blit(null, renderTexture, material);

      Texture2D texture = new Texture2D(resolution.x, resolution.y, TextureFormat.ARGB32, false);
      texture.filterMode = FilterMode.Bilinear;
      texture.wrapMode = TextureWrapMode.Clamp;
      RenderTexture.active = renderTexture;
      texture.ReadPixels(new Rect(Vector2.zero, resolution), 0, 0);

      RenderTexture.active = null;
      RenderTexture.ReleaseTemporary(renderTexture);
      texture.Apply();
      return texture;
    }

    public static Texture2D ResizeTexture(Texture2D originalTexture, int newWidth, int newHeight)
    {
      Material material = new Material(Shader.Find("Unlit/Transparent"));
      material.SetTexture("_MainTex", originalTexture);
      return RenderMaterial(ref material, new Vector2Int(newWidth, newHeight));
    }

    public static int GetSuperSampleValue()
    {
      string sample = superSampleOptions[selectedSuperSample].Substring(0, 1);
      return int.Parse(sample);
    }
  }
}

