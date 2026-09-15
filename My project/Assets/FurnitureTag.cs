using UnityEngine;

// 掛載在每個生成的傢俱上，用來記住它在資料庫裡的流水編號 (index)
public class FurnitureTag : MonoBehaviour
{
    public int index;
    public string url; // 雙重保險：用來確保就算後端沒給 index，我們也能靠網址認出它是誰
}
