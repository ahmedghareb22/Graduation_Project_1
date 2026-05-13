using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using TMPro;
using UnityEngine.UI;
using ArabicSupport;

[System.Serializable] public class ElevenLabsRequest { public string text; public string model_id = "eleven_multilingual_v2"; }
[System.Serializable] public class GeminiRequest { public Content[] contents; }
[System.Serializable] public class GeminiResponse { public Candidate[] candidates; }
[System.Serializable] public class Candidate { public Content content; }
[System.Serializable] public class Content { public Part[] parts; }
[System.Serializable] public class Part { public string text; }

[System.Serializable]
public class StaticVoice { public string textID; public AudioClip clip; }

public class GeminiManager : MonoBehaviour
{
    [Header("API Keys")]
    public ApiSettings apiData;

    private string geminiApiKey;
    private string elevenLabsApiKey;

    public string voiceId = "";
    public AudioSource audioSource;

    [Header("Static Audio Settings (The Mix)")]
    public StaticVoice[] staticVoices;

    [Header("UI & Character")]
    public Animator characterAnimator;
    public TMP_InputField userInputField;
    public TextMeshProUGUI chatDisplay;
    public ScrollRect scrollRect;
    public Button sendButton;
    public Button nextStatueButton; // السطر الجديد للزرار

    [Header("Lip Sync Settings")]
    public SkinnedMeshRenderer characterMesh;
    public int mouthBlendShapeIndex = 0;
    [Range(100f, 2500f)] public float sensitivity = 1500f;
    public float lerpSpeed = 20f;

    [HideInInspector] public string currentStatueContext = "";
    private bool isTyping = false;

    void Start()
    {
        if (apiData != null)
        {
            geminiApiKey = apiData.geminiApiKey;
            elevenLabsApiKey = apiData.elevenLabsApiKey;
            Debug.Log("<color=green>GeminiManager: تم تحميل الـ API Keys بنجاح.</color>");
        }
        else
        {
            Debug.LogError("GeminiManager: ملف الـ ApiSettings ناقص في الـ Inspector!");
        }

        if (sendButton != null) sendButton.onClick.AddListener(OnSendClick);
        // ربط الزرار الجديد بدالة الحركة
        if (nextStatueButton != null)
            nextStatueButton.onClick.AddListener(() => {
                var tour = GetComponent<TourManager>();
                if (tour != null) tour.HandleNavigationInput();
            });
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    void Update()
    {
        HandleLipSync();

        // السطر الجديد: الزرار لا يعمل إلا إذا كان حورس غير مشغول
        if (nextStatueButton != null)
            nextStatueButton.interactable = !IsCharacterBusy();

        if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
        {
            if (userInputField.interactable && !string.IsNullOrEmpty(userInputField.text) && !isTyping)
            {
                OnSendClick();
            }
        }
    }

    void HandleLipSync()
    {
        if (characterMesh == null || audioSource == null) return;
        float targetWeight = 0;
        if (audioSource.isPlaying)
        {
            float[] samples = new float[256];
            audioSource.GetOutputData(samples, 0);
            float vol = 0;
            foreach (var s in samples) vol += Mathf.Abs(s);
            targetWeight = Mathf.Clamp((vol / 256) * sensitivity, 0, 100);
        }
        float current = characterMesh.GetBlendShapeWeight(mouthBlendShapeIndex);
        characterMesh.SetBlendShapeWeight(mouthBlendShapeIndex, Mathf.Lerp(current, targetWeight, Time.deltaTime * lerpSpeed));
    }

    public void OnSendClick()
    {
        if (isTyping || string.IsNullOrEmpty(userInputField.text)) return;

        string msg = userInputField.text.Trim();
        Debug.Log($"GeminiManager: المستخدم أرسل رسالة: {msg}");

        var tour = GetComponent<TourManager>();

        chatDisplay.text += $"\n<color=#00FF00><b>{FixArabic("أنت:")}</b></color> {FixArabic(msg)}";
        userInputField.text = "";
        UpdateScroll();

        if (msg.Contains("يلا") || msg.Contains("ابدأ") || msg.Contains("بعده") || msg.Contains("التالي"))
        {
            Debug.Log("GeminiManager: تم اكتشاف أمر حركة، التوجه للـ TourManager.");
            if (tour != null) tour.HandleNavigationInput();
        }
        else
        {
            StartCoroutine(PostToGemini(msg));
        }
    }

    IEnumerator PostToGemini(string prompt)
    {
        isTyping = true;
        userInputField.interactable = false;

        string loading = $"\n<color=#FFFF00><b>{FixArabic("حورس:")}</b></color> {FixArabic("يفكر...")}";
        chatDisplay.text += loading; UpdateScroll();

        string context = $"أنت حورَس، مرشد سياحي مصري. المكان: ({currentStatueContext}). رد بلهجة مصرية خفيفة جداً ومختصرة.";
        GeminiRequest req = new GeminiRequest { contents = new Content[] { new Content { parts = new Part[] { new Part { text = context + " السؤال: " + prompt } } } } };

        // ملاحظة: تأكد من إصدار الموديل (مثلاً gemini-1.5-flash)
        string url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key=" + geminiApiKey;

        Debug.Log($"GeminiManager: جاري إرسال الطلب لـ Gemini...");

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(req));
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            chatDisplay.text = chatDisplay.text.Replace(loading, "");

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("GeminiManager: استجابة Gemini وصلت بنجاح.");

                string reply = "";
                bool parseSuccess = false;

                try
                {
                    GeminiResponse resp = JsonUtility.FromJson<GeminiResponse>(request.downloadHandler.text);
                    reply = resp.candidates[0].content.parts[0].text;
                    parseSuccess = true;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"GeminiManager: فشل في معالجة الـ JSON. الخطأ: {e.Message}");
                }

                // الـ yield return هنا أصبح خارج الـ try-catch فسيتم قبوله
                if (parseSuccess)
                {
                    Debug.Log($"GeminiManager: رد حورس: {reply}");
                    yield return StartCoroutine(SpeakAndType(reply));
                }
            }
            else
            {
                Debug.LogError($"GeminiManager: خطأ في طلب Gemini! الكود: {request.responseCode} - الخطأ: {request.error}");
            }
        }
        isTyping = false;
        userInputField.interactable = true; // نفتح الإدخال مجدداً في حال حدوث خطأ
    }

    public IEnumerator SpeakAndType(string text, bool playAnim = true, AudioClip forcedClip = null)
    {
        if (userInputField != null) userInputField.interactable = false;
        if (sendButton != null) sendButton.interactable = false;

        if (characterAnimator != null && playAnim) characterAnimator.SetBool("isSpeaking", true);
        StartCoroutine(TypeText(text));

        AudioClip clipToPlay = null;
        if (forcedClip != null)
        {
            clipToPlay = forcedClip;
            Debug.Log("GeminiManager: استخدام ملف صوتي محدد مسبقاً (Forced Clip).");
        }
        else
        {
            foreach (var sv in staticVoices)
            {
                if (text.Trim() == sv.textID.Trim() || text.Contains(sv.textID))
                {
                    clipToPlay = sv.clip;
                    Debug.Log($"GeminiManager: تم العثور على صوت مسجل مسبقاً للكلمة: {sv.textID}");
                    break;
                }
            }
        }

        if (clipToPlay != null)
        {
            audioSource.clip = clipToPlay;
            audioSource.Play();
        }
        else
        {
            Debug.Log("GeminiManager: لا يوجد صوت مسجل، جاري طلب الصوت من ElevenLabs...");
            yield return StartCoroutine(PlayVoice(text));
        }

        while (audioSource.isPlaying) yield return null;

        if (characterAnimator != null) characterAnimator.SetBool("isSpeaking", false);

        if (userInputField != null)
        {
            userInputField.interactable = true;
            userInputField.ActivateInputField();
        }
        if (sendButton != null) sendButton.interactable = true;
    }

    IEnumerator PlayVoice(string text)
    {
        if (string.IsNullOrEmpty(voiceId))
        {
            Debug.LogError("GeminiManager: الـ Voice ID فاضي! مش هعرف أنادي ElevenLabs.");
            yield break;
        }

        string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";
        ElevenLabsRequest req = new ElevenLabsRequest { text = text };

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(req)));
            request.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("xi-api-key", elevenLabsApiKey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("GeminiManager: تم تحميل الصوت من ElevenLabs بنجاح.");
                audioSource.clip = DownloadHandlerAudioClip.GetContent(request);
                audioSource.Play();
            }
            else
            {
                Debug.LogError($"GeminiManager: خطأ في ElevenLabs! الكود: {request.responseCode} - الخطأ: {request.error}");
                Debug.LogError($"الرد: {request.downloadHandler.text}");
            }
        }
    }

    IEnumerator TypeText(string fullText)
    {
        string header = $"\n<color=#FFFF00><b>{FixArabic("حورس:")}</b></color> ";
        chatDisplay.text += header;
        int startPos = chatDisplay.text.Length;
        string current = "";
        foreach (string word in fullText.Split(' '))
        {
            current += word + " ";
            chatDisplay.text = chatDisplay.text.Substring(0, startPos) + FixArabic(current);
            UpdateScroll();
            yield return new WaitForSeconds(0.05f);
        }
    }

    public bool IsCharacterBusy() => (audioSource != null && audioSource.isPlaying) || isTyping;

    string FixArabic(string input) => ArabicFixer.Fix(input, false, true);
    void UpdateScroll() { Canvas.ForceUpdateCanvases(); if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f; }
}