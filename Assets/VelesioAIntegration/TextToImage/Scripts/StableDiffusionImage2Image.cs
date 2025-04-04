using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using UnityEngine;
using System.Linq;
using System.Text;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Threading.Tasks;
using Math = System.Math;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Component to help generate a UI Image or RawImage using Stable Diffusion via Img2Img.
/// </summary>
[ExecuteAlways]
public class StableDiffusionImage2Image: StableDiffusionGenerator
{
    [ReadOnly]
    public string guid = "";

    public Texture2D inputTexture;
    public string prompt;
    public string negativePrompt;

    /// <summary>
    /// List of samplers to display as Drop-Down in the inspector
    /// </summary>
    [SerializeField]
    public string[] samplersList
    {
        get
        {
            if (sdc == null)
                sdc = GameObject.FindAnyObjectByType<StableDiffusionConfiguration>();
            return sdc.samplers;
        }
    }
    /// <summary>
    /// Actual sampler selected in the drop-down list
    /// </summary>
    [HideInInspector]
    public int selectedSampler = 0;

    public int width = 512;
    public int height = 512;
    public int steps = 50;
    public float cfgScale = 7;
    public long seed = -1;

    public long generatedSeed = -1;

    string filename = "";



    /// <summary>
    /// List of models to display as Drop-Down in the inspector
    /// </summary>
    [SerializeField]
    public string[] modelsList
    {
        get
        {
            if (sdc == null)
                sdc = GameObject.FindAnyObjectByType<StableDiffusionConfiguration>();
            return sdc.modelNames;
        }
    }
    /// <summary>
    /// Actual model selected in the drop-down list
    /// </summary>
    [HideInInspector]
    public int selectedModel = 0;


    /// <summary>
    /// On Awake, fill the properties with default values from the selected settings.
    /// </summary>
    void Awake()
    {
#if UNITY_EDITOR
        if (width < 0 || height < 0)
        {
            StableDiffusionConfiguration sdc = GameObject.FindAnyObjectByType<StableDiffusionConfiguration>();
            if (sdc != null)
            {
                SDSettings settings = sdc.settings;
                if (settings != null)
                {

                    width = settings.width;
                    height = settings.height;
                    steps = settings.steps;
                    cfgScale = settings.cfgScale;
                    seed = settings.seed;
                    return;
                }
            }

            width = 512;
            height = 512;
            steps = 50;
            cfgScale = 7;
            seed = -1;
        }
#endif
    }


    void Update()
    {
#if UNITY_EDITOR
        // Clamp image dimensions values between 128 and 2048 pixels
        if (width < 128) width = 128;
        if (height < 128) height = 128;
        if (width > 2048) width = 2048;
        if (height > 2048) height = 2048;

        // If not setup already, generate a GUID (Global Unique Identifier)
        if (guid == "")
            guid = Guid.NewGuid().ToString();
#endif
    }

    // Internally keep tracking if we are currently generating (prevent re-entry)
    bool generating = false;

    /// <summary>
    /// Callback function for the inspector Generate button.
    /// </summary>
    public void Generate()
    {
        // Start generation asynchronously
        if (!generating && !string.IsNullOrEmpty(prompt) && inputTexture)
        {
            if (!inputTexture.isReadable)
            {
                Debug.LogError($"Input Image {inputTexture.name} isn't readable. Go to texture import settings and tick the Read/Write box", this);
                return;
            }
            StartCoroutine(GenerateAsync());
        }
    }

    /// <summary>
    /// Setup the output path and filename for image generation
    /// </summary>
    void SetupFolders()
    {
        // Get the configuration settings
        if (sdc == null)
            sdc = GameObject.FindAnyObjectByType<StableDiffusionConfiguration>();

        try
        {
            // Determine output path
            string root = Application.dataPath + sdc.settings.OutputFolder;
            if (root == "" || !Directory.Exists(root))
                root = Application.streamingAssetsPath;
            string mat = Path.Combine(root, "SDImages");
            filename = Path.Combine(mat, guid + ".png");

            // If folders not already exists, create them
            if (!Directory.Exists(root))
                Directory.CreateDirectory(root);
            if (!Directory.Exists(mat))
                Directory.CreateDirectory(mat);

            // If the file already exists, delete it
            if (File.Exists(filename))
                File.Delete(filename);
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message + "\n\n" + e.StackTrace);
        }
    }

    IEnumerator GenerateAsync()
    {
        generating = true;

        SetupFolders();
        
        // Set the model parameters
        yield return sdc.SetModelAsync(modelsList[selectedModel]);

        // Generate the image using UnityWebRequest for better compatibility with cloud-hosted services
        string url = sdc.settings.StableDiffusionServerURL + sdc.settings.ImageToImageAPI;
        Debug.Log("Sending request to: " + url);
        
        // Prepare the request data
        byte[] inputImgBytes = inputTexture.EncodeToPNG();
        string inputImgString = Convert.ToBase64String(inputImgBytes);

        SDParamsInImg2Img sd = new SDParamsInImg2Img();
        sd.init_images = new string[] { inputImgString };
        sd.prompt = prompt;
        sd.negative_prompt = negativePrompt;
        sd.steps = steps;
        sd.cfg_scale = cfgScale;
        sd.width = width;
        sd.height = height;
        sd.seed = seed;
        sd.tiling = false;

        if (selectedSampler >= 0 && selectedSampler < samplersList.Length)
            sd.sampler_name = samplersList[selectedSampler];

        // Serialize the input parameters
        string jsonData = JsonConvert.SerializeObject(sd);
        Debug.Log("Sending JSON data (truncated): " + jsonData.Substring(0, Math.Min(100, jsonData.Length)) + "...");
        
        // Create the request
        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.downloadHandler = new DownloadHandlerBuffer();
        request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonData));
        request.SetRequestHeader("Content-Type", "application/json");
        
        // Add authentication if needed
        if (sdc.settings.useAuth && !sdc.settings.user.Equals("") && !sdc.settings.pass.Equals(""))
        {
            byte[] bytesToEncode = Encoding.UTF8.GetBytes(sdc.settings.user + ":" + sdc.settings.pass);
            string encodedCredentials = Convert.ToBase64String(bytesToEncode);
            request.SetRequestHeader("Authorization", "Basic " + encodedCredentials);
        }
        
        // Send the request
        request.SendWebRequest();
        
        // Wait for the request to complete while showing progress
        while (!request.isDone)
        {
            if (sdc.settings.useAuth && !sdc.settings.user.Equals("") && !sdc.settings.pass.Equals(""))
                UpdateGenerationProgressWithAuth();
            else
                UpdateGenerationProgress();
                
            yield return new WaitForSeconds(0.5f);
        }
        
        // Check if the request was successful
        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Request error: " + request.error);
            Debug.LogError("Response code: " + request.responseCode);
            Debug.LogError("Response: " + request.downloadHandler.text);
            
            if (request.responseCode == 405)
            {
                Debug.LogError("405 Method Not Allowed - This is common with RunPod. Make sure you launched with --api flag and check your firewall/proxy settings.");
                Debug.LogError("For RunPod, you might need to configure CORS settings or use their API proxy.");
            }
            
            generating = false;
#if UNITY_EDITOR
            EditorUtility.ClearProgressBar();
#endif
            yield break;
        }
        
        string responseText = request.downloadHandler.text;
        Debug.Log("Response received: " + responseText.Substring(0, Math.Min(100, responseText.Length)) + "...");

        try {
            // Deserialize the JSON string into a data structure
            SDResponseImg2Img json = JsonConvert.DeserializeObject<SDResponseImg2Img>(responseText);

            // If no image, there was probably an error so abort
            if (json.images == null || json.images.Length == 0)
            {
                Debug.LogError("No image was returned by the server. Verify that the server is correctly setup.");
                Debug.LogError("Full response: " + responseText);

                generating = false;
#if UNITY_EDITOR
                EditorUtility.ClearProgressBar();
#endif
                yield break;
            }

            // Decode the image from Base64 string into an array of bytes
            byte[] imageData = Convert.FromBase64String(json.images[0]);

            // Write it in the specified project output folder
            WriteImageFile(imageData, filename);

            // Read back the image into a texture
            if (File.Exists(filename))
            {
                Texture2D texture = new Texture2D(2, 2);
                texture.LoadImage(imageData);
                texture.Apply();

                LoadIntoImage(texture);
            }

            // Read the generation info back (only seed should have changed, as the generation picked a particular seed)
            if (json.info != "")
            {
                SDParamsOutTxt2Img info = JsonConvert.DeserializeObject<SDParamsOutTxt2Img>(json.info);

                // Read the seed that was used by Stable Diffusion to generate this result
                generatedSeed = info.seed;
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error processing response: " + e.Message);
            Debug.LogError("Stack trace: " + e.StackTrace);
            Debug.LogError("Response was: " + responseText);
        }
        
#if UNITY_EDITOR
        EditorUtility.ClearProgressBar();
#endif
        generating = false;
        yield return null;
    }

    /// <summary>
    /// Helper method to write image data to file without using yield within a try block
    /// </summary>
    /// <param name="imageData">Image data as byte array</param>
    /// <param name="filepath">Path to write the file</param>
    private void WriteImageFile(byte[] imageData, string filepath)
    {
        try 
        {
            using (FileStream imageFile = new FileStream(filepath, FileMode.Create))
            {
#if UNITY_EDITOR
                AssetDatabase.StartAssetEditing();
#endif
                imageFile.Write(imageData, 0, imageData.Length);
                imageFile.Flush();
#if UNITY_EDITOR
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
#endif
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error writing image file: " + e.Message);
        }
    }
    
    /// <summary>
    /// Load the texture into an Image or RawImage.
    /// </summary>
    /// <param name="texture">Texture to setup</param>
    void LoadIntoImage(Texture2D texture)
    {
        try
        {
            // Find the image component
            Image im = GetComponent<Image>();
            if (im != null)
            {
                // Create a new Sprite from the loaded image
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero);

                // Set the sprite as the source for the UI Image
                im.sprite = sprite;
            }
            // If no image found, try to find a RawImage component
            else
            {
                RawImage rim = GetComponent<RawImage>();
                if (rim != null)
                {
                    rim.texture = texture;
                }
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // Force Unity inspector to refresh with new asset
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                EditorApplication.QueuePlayerLoopUpdate();
                EditorSceneManager.MarkAllScenesDirty();
                EditorUtility.RequestScriptReload();
            }
#endif
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message + "\n\n" + e.StackTrace);
        }
    }
}
