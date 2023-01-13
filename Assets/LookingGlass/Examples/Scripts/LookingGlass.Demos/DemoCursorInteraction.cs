//Copyright 2017-2021 Looking Glass Factory Inc.
//All rights reserved.
//Unauthorized copying or distribution of this file, and the source code contained herein, is strictly prohibited.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LookingGlass.Demos {
	public class DemoCursorInteraction : MonoBehaviour {
		public GameObject hologramCamera;
		public LookingGlass.Cursor3D cursor;

		private Vector3 nextPosition = Vector3.back;

		void Update () {
			if (Input.GetMouseButtonDown(0)) {
				nextPosition = cursor.GetWorldPos();
			}
			hologramCamera.transform.position = Vector3.Slerp(hologramCamera.transform.position, nextPosition, 0.1f);
			hologramCamera.transform.LookAt(Vector3.zero);
		}
	}
}