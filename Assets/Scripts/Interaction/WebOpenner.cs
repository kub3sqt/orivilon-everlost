using UnityEngine;

namespace Orivilon
{
    public class WebOpenner : MonoBehaviour
    {
        public void OpenURL(string url)
        {
            Application.OpenURL(url);
        }
    }
}
