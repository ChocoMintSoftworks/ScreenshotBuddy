using System.Collections;
using System.Linq;
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
#if URP_AVAILABLE
using UnityEngine.Rendering.Universal;
#endif

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

    public static string screenshotFolder = "../Screenshots/";
    public static bool enableTransparency = false;
    public static string[] superSampleOptions = new string[] { "1x", "2x", "3x", "4x" };
    public static int selectedSuperSample = 0;
    public static bool disableOverlayCameras = false;
    private static bool ppOriginalState = false;
    private static bool enableGameLikeCamera = false;
    private static bool previousGridSetting = false;

    public static Color32 overlayColor = new Color32(255, 0, 0, 80);

    [MenuItem("Window/ScreenshotBuddy")]
    public static void ShowWindow()
    {
      GetWindow<ScreenshotBuddy>("ScreenshotBuddy");
    }

    // Load settings here. Using OnEnable because Awake does not 
    // trigger on script recompilation
    public void OnEnable()
    {
      LoadAllSettings();
    }

    ////// RENDER GUI HERE
    void OnGUI()
    {
      EditorGUI.BeginChangeCheck();
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
      disableOverlayCameras = GUILayout.Toggle(disableOverlayCameras, "Disable Overlay Cameras");
      if (enableTransparency)
      {
        GUILayout.Label("Notice: Enabling transparency disables post processing due to a unity bug.", EditorStyles.label);
      }
      GUILayout.BeginHorizontal();
      GUILayout.Label("Filename Prefix:", EditorStyles.label);
      userFilePrefix = GUILayout.TextField(userFilePrefix, 32);
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GUILayout.Label("Screenshot Path:", EditorStyles.label);
      screenshotFolder = GUILayout.TextField(screenshotFolder, 256);
      if (GUILayout.Button("Browse", GUILayout.ExpandWidth(false)))
      {
        screenshotFolder = PickScreenshotFolder();
      }
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
      if (GUILayout.Button("Toggle Camera Mode", GUILayout.ExpandWidth(false)))
      {
        ToggleCameraMode();
      }
      if (GUILayout.Button("Reset Path", GUILayout.ExpandWidth(false)))
      {
        ResetScreenshotPath();
      }
      GUILayout.EndHorizontal();

      GUILayout.Label("Resolution Presets", EditorStyles.boldLabel);
      GUILayout.BeginHorizontal();
      if (GUILayout.Button("1080p", GUILayout.ExpandWidth(false)))
      {
        SetResolutionPreset(1920, 1080);
      }
      if (GUILayout.Button("1440p", GUILayout.ExpandWidth(false)))
      {
        SetResolutionPreset(2560, 1440);
      }
      if (GUILayout.Button("2160p", GUILayout.ExpandWidth(false)))
      {
        SetResolutionPreset(3840, 2160);
      }
      if (GUILayout.Button("Instagram Square", GUILayout.ExpandWidth(false)))
      {
        SetResolutionPreset(1080, 1080);
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

      if (EditorGUI.EndChangeCheck())
      {
        // Saving everything at once, for now.
        SaveAllSettings();
      }
    }

    static void OpenScreenshotFolder()
    {
      if (!Directory.Exists(screenshotFolder))
      {
        Directory.CreateDirectory(screenshotFolder);
      }

      string firstFile = Directory.EnumerateFiles(screenshotFolder).FirstOrDefault();
      if (!string.IsNullOrEmpty(firstFile))
        EditorUtility.RevealInFinder(Path.Combine(screenshotFolder, firstFile));
      else
        EditorUtility.RevealInFinder(screenshotFolder);
    }

    static void Screenshot()
    {
      bool screenshotTaken = CaptureEditorScreenshot(userFilePrefix);
    }

    public static bool CaptureEditorScreenshot(string filePrefix)
    {
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

      var renderDesc = new RenderTextureDescriptor(resWidth * GetSuperSampleValue(), resHeight * GetSuperSampleValue(), RenderTextureFormat.ARGB32, 24);
      renderDesc.sRGB = true;

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

#if URP_AVAILABLE
      if (IsURP() && disableOverlayCameras)
      {
        cam.GetUniversalAdditionalCameraData().cameraStack.Clear();
      }
#endif

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

    public static void SetResolutionPreset(int width, int height)
    {
      resWidth = width;
      resHeight = height;
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


    private static void ToggleCameraMode()
    {
      SceneView sw = SceneView.lastActiveSceneView;

      if (sw == null)
      {
        Debug.LogError("no scene view found. Make sure you have a scene view window opened.");
        return;
      }

      Camera cam = sw.camera;

      if (cam == null)
      {
        Debug.LogError("no camera attached to current scene view.");
        return;
      }

      if (!enableGameLikeCamera)
      {
        previousGridSetting = sw.showGrid;
        enableGameLikeCamera = true;
        cam.cameraType = CameraType.Game;
        sw.showGrid = false;
#if URP_AVAILABLE
        if (IsURP() && disableOverlayCameras)
        {
          cam.GetUniversalAdditionalCameraData().cameraStack.Clear();
        }
#endif
      }
      else
      {
        sw.showGrid = previousGridSetting;
        enableGameLikeCamera = false;
        cam.cameraType = CameraType.SceneView;
      }

    }

    // Original source of the texture resizing code:
    // https://forum.unity.com/threads/how-to-resize-scale-down-texture-without-losing-quality.976965/
    public static Texture2D RenderMaterial(ref Material material, Vector2Int resolution)
    {
      RenderTexture renderTexture = RenderTexture.GetTemporary(resolution.x, resolution.y);
      Graphics.Blit(null, renderTexture, material);

      Texture2D texture = new Texture2D(resolution.x, resolution.y, TextureFormat.ARGB32, false);
      texture.filterMode = FilterMode.Trilinear;
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

    private static bool IsURP()
    {
      string assetTypeURP = "UniversalRenderPipelineAsset";
      return GraphicsSettings.renderPipelineAsset.GetType().Name.Contains(assetTypeURP);
    }

    private static string PickScreenshotFolder()
    {
      string path = EditorUtility.OpenFolderPanel("Save screenshots to", screenshotFolder, "");
      return path + "/";
    }

    private static void ResetScreenshotPath()
    {
      screenshotFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/"));
    }

    private static string SettingsPrefix()
    {
      return PlayerSettings.companyName + "." + PlayerSettings.productName + ".";
    }

    private static void SaveInt(string key, int value)
    {
      EditorPrefs.SetInt(SettingsPrefix() + key, value);
    }

    private static void SaveBool(string key, bool value)
    {
      EditorPrefs.SetBool(SettingsPrefix() + key, value);
    }

    private static void SaveString(string key, string value)
    {
      EditorPrefs.SetString(SettingsPrefix() + key, value);
    }

    private static void SaveColor(string key, Color32 value)
    {
      EditorPrefs.SetInt(SettingsPrefix() + key + "_R", value.r);
      EditorPrefs.SetInt(SettingsPrefix() + key + "_G", value.g);
      EditorPrefs.SetInt(SettingsPrefix() + key + "_B", value.b);
      EditorPrefs.SetInt(SettingsPrefix() + key + "_A", value.a);
    }

    private static Color32 LoadColor(string key)
    {
      Color32 loadedColor = new Color32(
      (byte)EditorPrefs.GetInt(SettingsPrefix() + key + "_R", 255),
      (byte)EditorPrefs.GetInt(SettingsPrefix() + key + "_G", 0),
      (byte)EditorPrefs.GetInt(SettingsPrefix() + key + "_B", 0),
      (byte)EditorPrefs.GetInt(SettingsPrefix() + key + "_A", 80));
      return loadedColor;
    }

    private static void SaveAllSettings()
    {
      SaveInt("resWidth", resWidth);
      SaveInt("resHeight", resHeight);
      SaveInt("jpgQuality", jpgQuality);
      SaveInt("selectedFormat", selectedFormat);
      SaveInt("selectedMsaaSamples", selectedMsaaSamples);
      SaveInt("selectedSuperSample", selectedSuperSample);
      SaveBool("disableOverlayCameras", disableOverlayCameras);
      SaveBool("enableTransparency", enableTransparency);
      SaveString("userFilePrefix", userFilePrefix);
      SaveColor("overlayColor", overlayColor);
      SaveString("screenshotFolder", screenshotFolder);
    }

    private static void LoadAllSettings()
    {
      resWidth = EditorPrefs.GetInt(SettingsPrefix() + "resWidth", 1920);
      resHeight = EditorPrefs.GetInt(SettingsPrefix() + "resHeight", 1080);
      jpgQuality = EditorPrefs.GetInt(SettingsPrefix() + "jpgQuality", 100);
      selectedFormat = EditorPrefs.GetInt(SettingsPrefix() + "selectedFormat", 0);
      selectedMsaaSamples = EditorPrefs.GetInt(SettingsPrefix() + "selectedMsaaSamples", 3);
      selectedSuperSample = EditorPrefs.GetInt(SettingsPrefix() + "selectedSuperSample", 0);
      disableOverlayCameras = EditorPrefs.GetBool(SettingsPrefix() + "disableOverlayCameras", false);
      enableTransparency = EditorPrefs.GetBool(SettingsPrefix() + "enableTransparency", false);
      userFilePrefix = EditorPrefs.GetString(SettingsPrefix() + "userFilePrefix", "screenshot");
      overlayColor = LoadColor("overlayColor");
      screenshotFolder = EditorPrefs.GetString(SettingsPrefix() + "screenshotFolder", Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/")));
    }
  }
}