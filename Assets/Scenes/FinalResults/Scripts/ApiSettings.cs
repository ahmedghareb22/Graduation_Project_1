using UnityEngine;

[CreateAssetMenu(fileName = "ApiSettings", menuName = "Custom/ApiSettings")]
public class ApiSettings : ScriptableObject
{
    public string geminiApiKey;
    public string elevenLabsApiKey;
}