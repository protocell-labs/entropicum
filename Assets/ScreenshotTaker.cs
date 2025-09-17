using UnityEngine;
using System.IO;

public class ScreenshotTaker : MonoBehaviour
{
    [SerializeField] private KeyCode screenshotKey = KeyCode.P;

    // Absolute path to save screenshots
    private string folderPath = @"C:\Users\lukap\Downloads";

    private void Update()
    {
        if (Input.GetKeyDown(screenshotKey))
        {
            TakeScreenshot();
        }
    }

    private void TakeScreenshot()
    {
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        string filename = $"entropicum_wip_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
        string filePath = Path.Combine(folderPath, filename);

        ScreenCapture.CaptureScreenshot(filePath);
        Debug.Log($"Screenshot saved to: {filePath}");
    }
}
