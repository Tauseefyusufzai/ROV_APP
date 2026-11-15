using UnityEngine;
using System.Collections;
using System;
using System.Runtime.InteropServices;
using Ionic.Zip;
using System.Text;
using System.IO;
using System.ComponentModel;
 
public class ZipUtil
{
#if UNITY_IPHONE
    [DllImport("__Internal")]
    private static extern void unzip (string zipFilePath, string location);
 
    [DllImport("__Internal")]
    private static extern void zip (string zipFilePath);
 
    [DllImport("__Internal")]
    private static extern void addZipFile (string addFile);
 
#endif
 
    public static float progress = 0.0f;
 
    public static void ExtractFile(string zipToUnpack, string unpackDirectory)
    {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
        progress = 0.0f;
        Directory.CreateDirectory(unpackDirectory);
 
        BackgroundWorker worker = new BackgroundWorker();
        worker.DoWork += (o, e) =>
        {
            using (ZipFile zip = ZipFile.Read(zipToUnpack))
            {
                int step = 0;
 
                foreach (ZipEntry file in zip)
                {
                    file.Extract(unpackDirectory, ExtractExistingFileAction.OverwriteSilently);
                    step++;
                    progress = step / (zip.Count * 1.0f);
                }
            }
        };
     
        worker.RunWorkerAsync();
#endif
    }
 
 
    public static void Unzip (string zipFilePath, string location)
    {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
        Directory.CreateDirectory (location);
     
        using (ZipFile zip = ZipFile.Read (zipFilePath)) {
         
            zip.ExtractAll (location, ExtractExistingFileAction.OverwriteSilently);
        }
#elif UNITY_ANDROID
        using (AndroidJavaClass zipper = new AndroidJavaClass ("com.tsw.zipper")) {
            zipper.CallStatic ("unzip", zipFilePath, location);
        }
#elif UNITY_IPHONE
        unzip (zipFilePath, location);
#endif
    }
 
    public static void Zip (string zipFileName, params string[] files)
    {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
        string path = Path.GetDirectoryName(zipFileName);
        Directory.CreateDirectory (path);
     
        using (ZipFile zip = new ZipFile()) {
            foreach (string file in files) {
                zip.AddFile(file, "");
            }
            zip.Save (zipFileName);
        }
#elif UNITY_ANDROID
        using (AndroidJavaClass zipper = new AndroidJavaClass ("com.tsw.zipper")) {
            {
                zipper.CallStatic ("zip", zipFileName, files);
            }
        }
#elif UNITY_IPHONE
        foreach (string file in files) {
            addZipFile (file);
        }
        zip (zipFileName);
#endif
    }
}
 