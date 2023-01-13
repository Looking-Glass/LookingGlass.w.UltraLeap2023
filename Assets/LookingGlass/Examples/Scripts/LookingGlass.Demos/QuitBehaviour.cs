using UnityEngine;

namespace LookingGlass.Demos {
    public class QuitBehaviour : MonoBehaviour {
        private void Update() {
            if (Input.GetKeyDown(KeyCode.Escape))
                Application.Quit();
        }
    }
}
