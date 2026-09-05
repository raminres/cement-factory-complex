using UnityEngine;
using System.Collections;

public class StartC : MonoBehaviour {

public GameObject obj;
	void  Start (){
		obj.SetActive(true);
	}
	void  OnTriggerEnter (){
		if (obj.activeInHierarchy == true){

			obj.SetActive (false);
		}
		else if (obj.activeInHierarchy == false){

			obj.SetActive (true);
		}


	}
}