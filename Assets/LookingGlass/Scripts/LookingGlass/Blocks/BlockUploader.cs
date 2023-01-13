using UnityEngine;

namespace LookingGlass.Blocks {
    public class BlockUploader : MonoBehaviour {
        [SerializeField] internal string quiltFilePath;
        [SerializeField] internal string blockTitle;
        [TextArea(5, 8)]
        [SerializeField] internal string blockDescription;

        public string QuiltFilePath => quiltFilePath;
        public string BlockTitle => blockTitle;
        public string BlockDescription => blockDescription;
    }
}
