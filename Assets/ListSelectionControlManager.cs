using ListView;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ListSelectionControlManager : MonoBehaviour {

    private bool choosing = true;
    private bool downloading = false;
    private bool updatedData = false;
    private bool viewing = false;

    private string webRootLocation;

    private bool triggerPressed = false;

    public GameObject listView;
    public GameObject textView;
    public SteamVR_TrackedObject controllerRightObject;
    public GameObject pointCloudHolder;

    private GameObject selectedListObj = null;
    private SteamVR_TrackedController controller;
    private SteamVR_LaserPointer pointer;
    private TextMesh infoTextMesh;
    private bool m_WebLock;
    private float progress;

    private List<Vector4> pointData = null;


    public Material pointCloudMaterial;

    // maintained reference to the generated children (point cloud objects)
    private GameObject[] pointClouds;
    // the number of points read from the data file
    int numPoints = 0;
    // the number of point clouds that will be generated from the collection of points
    int numDivisions = 0;
    // The maximum number of vertices unity will allow per single mesh
    const int MAX_NUMBER_OF_POINTS_PER_MESH = 65000;




    private string WEB_DL_LOCATION;

    // Use this for initialization
    void Start () {
        pointer = controllerRightObject.GetComponent<SteamVR_LaserPointer>();
        pointer.PointerIn += PointerInDelegate;
        pointer.PointerOut += PointerOutDelegate;
        controller = pointer.GetComponent<SteamVR_TrackedController>();
        controller.TriggerClicked += TriggerDelegate;
        controller.MenuButtonClicked += MenuButtonDelegate;
        infoTextMesh = textView.GetComponent<TextMesh>();
        webRootLocation = System.IO.File.ReadAllText("config.cfg");
        WebList webList = listView.GetComponent<WebList>();
        webList.Setup(webRootLocation);
        WEB_DL_LOCATION = webRootLocation + "/file_manager/download_file/csv/";
	}

    // Update is called once per frame
    void Update()
    {
        if (choosing)
        {
            if (triggerPressed)
            {
                if (selectedListObj != null)
                {
                    Component[] components = selectedListObj.GetComponents<Component>();
                    ListView.JSONItem selectedObjectJson = selectedListObj.GetComponent<ListView.JSONItem>();
                    string fileToGet = selectedObjectJson.data.text;
                    print("Selected: " + fileToGet);
                    downloading = true;
                    StartCoroutine(DownloadFile(fileToGet, data => { this.pointData = data; }));
                    choosing = false;
                    listView.SetActive(false);
                    infoTextMesh.text = "DOWNLOADING...";
                }
            }
        }else if (downloading)
        {
            infoTextMesh.text = "DOWNLOADING..." + 100.0f*progress+"%";
        }else if (updatedData)
        {
            textView.SetActive(false);
            StartCoroutine(ChangeDisplay());
            //ChangeDisplay();
            updatedData = false;
            viewing = true;
        } else if (viewing)
        {
            //controls while viewing:
            //trigger: move in direction controller is pointing, flying
            //pad click: move in direction controller is pointing, relative to clicked direction (left side strafes left).  do not adjust Y
            //grip: reset to default.
            if (controller.triggerPressed)
            {
                Vector3 moveVector = controller.transform.forward;
                moveVector = moveVector * -0.03f;
                pointCloudHolder.transform.Translate(moveVector);
            }
            else if (controller.padPressed)
            {
                Vector3 moveVector = controller.transform.forward;
                moveVector.y = 0;
                moveVector.Normalize();
                moveVector = moveVector * -0.03f;
                pointCloudHolder.transform.Translate(moveVector);
            }
            else if (controller.gripped)
            {
                pointCloudHolder.transform.localPosition = Vector3.zero;
            }
            
        }
        triggerPressed = false;
	}

    private void PointerInDelegate(object sender, PointerEventArgs e)
    {
        selectedListObj = e.target.gameObject;
    }

    private void PointerOutDelegate(object sender, PointerEventArgs e)
    {
        selectedListObj = null;
    }

    private void MenuButtonDelegate(object sender, ClickedEventArgs e)
    {
        choosing = true;
        listView.SetActive(true);
        infoTextMesh.text = "Existing Scans";
        downloading = false;
        updatedData = false;
        viewing = false;
        pointCloudHolder.transform.localPosition = Vector3.zero;
        foreach (Transform child in pointCloudHolder.transform)
        {
            if (child.gameObject.name.StartsWith("Cloud"))
            {
                GameObject.Destroy(child.gameObject);
            }
        }
        WebList webList = listView.GetComponent<WebList>();
        
        webList.Setup(webRootLocation);
    }

    private void TriggerDelegate(object sender, ClickedEventArgs e)
    {
        triggerPressed = true;
    }

    delegate void DataResult(List<Vector4> data);

    IEnumerator DownloadFile(string fileName, DataResult result)
    {
        if (m_WebLock)
            yield break;
        m_WebLock = true;

        WWW www = new WWW(WEB_DL_LOCATION+fileName);
        while (!www.isDone)
        {
            progress = www.progress;
            yield return null;
        }
        progress = 1.0f;
        string text = www.text;
        m_WebLock = false;
        if (downloading)
        {
            updatedData = true;
            result(CSVReader.ReadPointsFromString(text));
            downloading = false;
        }
    }

    IEnumerator ChangeDisplay()
    {
        numPoints = pointData.Count;

        List<List<Vector4>> pointPartitions = new List<List<Vector4>>();
        List<Vector4> partition = new List<Vector4>();
        for (int i = 0; i < numPoints; i++)
        {
            if (partition.Count >= MAX_NUMBER_OF_POINTS_PER_MESH)
            {
                pointPartitions.Add(partition);
                partition = new List<Vector4>();
            }
            partition.Add(pointData[i]);
        }
        pointPartitions.Add(partition);

        numDivisions = pointPartitions.Count;

        print("" + numPoints + " points, split into " + numDivisions + " clouds.");

        pointClouds = new GameObject[numDivisions];

        int cloudNumber = 0;
        foreach(List<Vector4> points in pointPartitions)
        {
            Vector3[] positions = new Vector3[points.Count];
            float[] normalizedSignalStrength = new float[points.Count];
            for(int i = 0; i < points.Count; i++)
            {
                // normalzied signal strength stored in the 4th component of the vector
                normalizedSignalStrength[i] = points[i].w * 0.3f;

                // position stored in the first 3 elements of the vector (conversion handled by implicit cast)
                positions[i] = points[i];
            }
            // Create the point cloud using the subset of data
            GameObject obj = new GameObject("Cloud "+cloudNumber);
            obj.transform.SetParent(pointCloudHolder.transform, false);
            obj.AddComponent<PointCloud>().CreateMesh(positions, normalizedSignalStrength);
            obj.GetComponent<MeshRenderer>().material = pointCloudMaterial;
            pointClouds[cloudNumber] = obj;
            cloudNumber++;
        }

        foreach(GameObject cloud in pointClouds)
        {
            cloud.transform.Translate(new Vector3(0, 0.4318f, 0));
        }
        yield break;
    }
}
