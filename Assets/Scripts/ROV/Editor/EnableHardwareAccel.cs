using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

/// <summary>
/// Editor post-process script to automatically inject android:hardwareAccelerated="true"
/// into the built AndroidManifest.xml. This is required because TextureView (used by
/// the ExoPlayer bridge plugin to retrieve video frames) requires hardware acceleration
/// to be enabled globally or at the Activity level.
/// </summary>
public class EnableHardwareAccel : IPostGenerateGradleAndroidProject
{
    public void OnPostGenerateGradleAndroidProject(string basePath)
    {
        string manifestPath = Path.Combine(basePath, "src/main/AndroidManifest.xml");
        if (!File.Exists(manifestPath))
        {
            Debug.LogError($"[EnableHardwareAccel] AndroidManifest.xml not found at: {manifestPath}");
            return;
        }

        try
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(manifestPath);

            XmlNodeList appList = doc.GetElementsByTagName("application");
            if (appList.Count > 0)
            {
                XmlNode applicationNode = appList[0];
                string ns = "http://schemas.android.com/apk/res/android";

                // 1. Enable hardware acceleration on <application>
                XmlAttribute appAttr = (XmlAttribute)applicationNode.Attributes.GetNamedItem("android:hardwareAccelerated");
                if (appAttr == null)
                {
                    appAttr = doc.CreateAttribute("android", "hardwareAccelerated", ns);
                    applicationNode.Attributes.Append(appAttr);
                }
                appAttr.Value = "true";

                // 2. Enable cleartext HTTP traffic on <application> (to support http:// video feeds)
                XmlAttribute cleartextAttr = (XmlAttribute)applicationNode.Attributes.GetNamedItem("android:usesCleartextTraffic");
                if (cleartextAttr == null)
                {
                    cleartextAttr = doc.CreateAttribute("android", "usesCleartextTraffic", ns);
                    applicationNode.Attributes.Append(cleartextAttr);
                }
                cleartextAttr.Value = "true";

                // 3. Enable hardware acceleration on all <activity> elements (including UnityPlayerActivity)
                XmlNodeList actList = doc.GetElementsByTagName("activity");
                for (int i = 0; i < actList.Count; i++)
                {
                    XmlNode activityNode = actList[i];
                    XmlAttribute actAttr = (XmlAttribute)activityNode.Attributes.GetNamedItem("android:hardwareAccelerated");
                    if (actAttr == null)
                    {
                        actAttr = doc.CreateAttribute("android", "hardwareAccelerated", ns);
                        activityNode.Attributes.Append(actAttr);
                    }
                    actAttr.Value = "true";
                }

                doc.Save(manifestPath);
                Debug.Log("[EnableHardwareAccel] Successfully enabled hardware acceleration and cleartext traffic in AndroidManifest.xml.");
            }
            else
            {
                Debug.LogError("[EnableHardwareAccel] No <application> tag found in AndroidManifest.xml.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[EnableHardwareAccel] Failed to modify AndroidManifest.xml: {ex.Message}");
        }
    }

    public int callbackOrder => 1;
}
