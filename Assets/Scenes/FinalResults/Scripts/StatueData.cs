using UnityEngine;

[CreateAssetMenu(fileName = "NewStatue", menuName = "Museum/Statue Data")]
public class StatueData : ScriptableObject
{
    public string statueName; // اسم التمثال
    [TextArea(5, 10)]
    public string info;       // المعلومات التاريخية

    [Header("Optional: Recorded Audio")]
    public AudioClip preRecordedAudio; // لو سجلت الصوت بنفسك حطه هنا
}