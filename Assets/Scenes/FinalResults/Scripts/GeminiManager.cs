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
    public ApiSettings apiData; // اسحب الملف هنا من الـ Inspector

    // تعريف المتغيرات هنا عشان الكلاس كله يشوفها
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

    [Header("Lip Sync Settings")]
    public SkinnedMeshRenderer characterMesh;
    public int mouthBlendShapeIndex = 0;
    [Range(100f, 2500f)] public float sensitivity = 1500f;
    public float lerpSpeed = 20f;

    [HideInInspector] public string currentStatueContext = "";
    private bool isTyping = false;

    void Start()
    {
        // هنا بنجيب القيم من ملف الـ ScriptableObject اللي عملناه
        if (apiData != null)
        {
            geminiApiKey = apiData.geminiApiKey;
            elevenLabsApiKey = apiData.elevenLabsApiKey;
        }
        else
        {
            Debug.LogError("يا بطل، إنت نسيت تسحب ملف الـ ApiSettings في الـ Inspector!");
        }

        if (sendButton != null) sendButton.onClick.AddListener(OnSendClick);
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        //StartCoroutine(DelayedGreeting());
    }

    void Update()
    {
        HandleLipSync();

        // إرسال الرسالة بـ Enter فقط لو الـ Input متاح ومش شغالين حالياً
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

    IEnumerator DelayedGreeting()
    {
        yield return new WaitForSeconds(2f);
        if (characterAnimator != null) characterAnimator.SetTrigger("Wave");
        yield return StartCoroutine(SpeakAndType("أهلاً بك يا صديقي! أنا حورس، مرشدك السياحي. جاهز نبدأ الجولة؟", false));
    }

    public void OnSendClick()
    {
        if (isTyping || string.IsNullOrEmpty(userInputField.text)) return;

        string msg = userInputField.text.Trim();
        var tour = GetComponent<TourManager>();

        chatDisplay.text += $"\n<color=#00FF00><b>{FixArabic("أنت:")}</b></color> {FixArabic(msg)}";
        userInputField.text = "";
        UpdateScroll();

        // فحص أوامر الحركة (يلا بينا، التالي، الخ)
        if (msg.Contains("يلا") || msg.Contains("ابدأ") || msg.Contains("بعده") || msg.Contains("التالي"))
        {
            if (tour != null)
            {
                // هننادي على الدالة اللي بتشيك لو حورس مشغول ولا لا
                tour.HandleNavigationInput();
            }
        }
        else
        {
            StartCoroutine(PostToGemini(msg));
        }
    }

    IEnumerator PostToGemini(string prompt)
    {
        isTyping = true;
        // قفل الـ InputField أثناء التفكير والرد
        userInputField.interactable = false;

        string loading = $"\n<color=#FFFF00><b>{FixArabic("حورس:")}</b></color> {FixArabic("يفكر...")}";
        chatDisplay.text += loading; UpdateScroll();

        string context = $"أنت حورَس، مرشد سياحي مصري. المكان: ({currentStatueContext}). رد بلهجة مصرية خفيفة جداً ومختصرة.";
        GeminiRequest req = new GeminiRequest { contents = new Content[] { new Content { parts = new Part[] { new Part { text = context + " السؤال: " + prompt } } } } };
        string url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key=" + geminiApiKey;

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
                GeminiResponse resp = JsonUtility.FromJson<GeminiResponse>(request.downloadHandler.text);
                yield return StartCoroutine(SpeakAndType(resp.candidates[0].content.parts[0].text));
            }
        }
        isTyping = false;
        // فتح الـ InputField بعد انتهاء الرد (يتم التحكم فيه داخل SpeakAndType أيضاً للأمان)
    }

    public IEnumerator SpeakAndType(string text, bool playAnim = true, AudioClip forcedClip = null)
    {
        // 1. قفل الـ Input Field والـ Button مع بعض
        if (userInputField != null) userInputField.interactable = false;
        if (sendButton != null) sendButton.interactable = false; // قفل زرار الإرسال

        if (characterAnimator != null && playAnim) characterAnimator.SetBool("isSpeaking", true);
        StartCoroutine(TypeText(text));

        AudioClip clipToPlay = null;
        if (forcedClip != null) clipToPlay = forcedClip;
        else
        {
            foreach (var sv in staticVoices)
            {
                if (text.Trim() == sv.textID.Trim() || text.Contains(sv.textID)) { clipToPlay = sv.clip; break; }
            }
        }

        if (clipToPlay != null) { audioSource.clip = clipToPlay; audioSource.Play(); }
        else { yield return StartCoroutine(PlayVoice(text)); }

        while (audioSource.isPlaying) yield return null;

        if (characterAnimator != null) characterAnimator.SetBool("isSpeaking", false);

        // 2. فتح الـ Input Field والـ Button وتركيز الماوس
        if (userInputField != null)
        {
            userInputField.interactable = true;
            userInputField.ActivateInputField();
        }
        if (sendButton != null) sendButton.interactable = true; // فتح الزرار تاني
    }

    IEnumerator PlayVoice(string text)
    {
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
                audioSource.clip = DownloadHandlerAudioClip.GetContent(request);
                audioSource.Play();
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

    // دالة بترجع true لو حورس بيتكلم أو لسه بيفكر في الرد
    public bool IsCharacterBusy()
    {
        // مشغول لو الأوديو شغال أو لو لسه بيكتب (isTyping)
        return (audioSource != null && audioSource.isPlaying) || isTyping;
    }

    string FixArabic(string input) => ArabicFixer.Fix(input, false, true);
    void UpdateScroll() { Canvas.ForceUpdateCanvases(); if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f; }
}