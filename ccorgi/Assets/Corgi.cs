using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

using UnityEngine;
using Newtonsoft.Json;

public delegate void FallbackAction(string url, int version, ResolveAction resolve);
public delegate void ResolveAction(Texture2D tex);

public class Corgi : MonoBehaviour
{
    private const string IndexFilename = "corgi_init_data.json";

    private class CacheHandle
    {
        public AutoResetEvent waitEvent;
        public CorgiCacheState state;

        public CacheHandle()
        {
            waitEvent = new AutoResetEvent(false);
            state = CorgiCacheState.None;
        }
    }

    private static Corgi _instance;
    public static Corgi instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("CORGI_CACHE");
                _instance = go.AddComponent<Corgi>();
            }
            return _instance;
        }
    }

    private int mainThreadId;
    private Dictionary<string, CacheHandle> handles;
    private List<Action> mainThreadTasks;

    CorgiDisk disk;
    CorgiMemory memory;
    CorgiWeb web;
    List<CorgiMemorize> memorize;
    FallbackAction fallback;

    void Awake()
    {
        if(_instance == null)
            _instance = this;
        DontDestroyOnLoad(this);

        mainThreadId = Thread.CurrentThread.ManagedThreadId;
        handles = new Dictionary<string, CacheHandle>();
        mainThreadTasks = new List<Action>();

        disk = gameObject.AddComponent<CorgiDisk>();
        memory = gameObject.AddComponent<CorgiMemory>();
        web = gameObject.AddComponent<CorgiWeb>();

        var path = Path.Combine(Application.persistentDataPath, IndexFilename);
        if (!File.Exists(path))
            return;

        var content = File.ReadAllText(path);
        Debug.Log("Awake Data" + content);

        if (string.IsNullOrEmpty(content))
            return;

        var initData = JsonConvert.DeserializeObject<CorgiDiskData>(content);
        if (initData == null)
            return;

        disk.Init(initData);
    }
    void OnDestroy()
    {
    }
    void Update()
    {

    }

    private void EnsureMainthread()
    {
        if (mainThreadId == Thread.CurrentThread.ManagedThreadId)
            throw new InvalidOperationException("This function must be called in main thread.");
    }

    public static void Fetch(string url, int version, ResolveAction resolve)
    {
        instance._Fetch(url, version, resolve);
    }
    void _Fetch(string url, int version, ResolveAction resolve)
    {
        EnsureMainthread();

        if (cacheState == CorgiCacheState.None ||
            cacheState == CorgiCacheState.EndFetching)
        {

            if (cacheState == CorgiCacheState.None)
                handles[url] = new CacheHandle();

            memory.Load(url, version, resolve, OnMemoryFaield);
        }
        else
        {
            var handle = handles[url];
            new Thread(() =>
            {
                handle.waitEvent.WaitOne(2000);
                mainThreadTasks.Add(() => {
                    memory.Load(url, version, resolve, OnMemoryFaield);
                });
            }).Start();
        }
    }

    void OnMemoryFaield(string url, int version, ResolveAction resolve)
    {
        disk.Load(url, version, resolve, OnDiskFailed);
    }

    void OnDiskFailed(string url, int version, ResolveAction resolve)
    {
        web.Load(url, version, resolve, fallback);
    }

    /*
    public static void AddCacheLayer(int priority, FallbackAction action)
    {
        instance._AddCacheLayer(priority, action);
    }

    void _AddCacheLayer(int priority, FallbackAction action)
    {
    }
    */

    public static void Fallback(FallbackAction fallback)
    {
        instance._Fallback(fallback);
    }

    void _Fallback(FallbackAction _fallback)
    {
        EnsureMainthread();
        _fallback += _fallback;
    }

    public static void Memorize(List<CorgiMemorize> memo)
    {
        instance._Memorize(memo);
    }

    void _Memorize(List<CorgiMemorize> memo)
    {
        EnsureMainthread();
        foreach (var m in memo)
        {
            _Fetch(m.url, m.version, (tex) => { });
        }
    }

    public static void SaveData()
    {
        _instance._SaveData();
    }

    public void _SaveData()
    {
        EnsureMainthread();

        CorgiDiskData saveData = disk.GetData();
        if (saveData == null)
            return;

        var path = Path.Combine(Application.persistentDataPath, IndexFilename);
        var t = new Thread(() =>
        {
            var content = JsonConvert.SerializeObject(saveData);
            Debug.Log(content);
            File.WriteAllText(path, content);
        });
        t.Start();
    }
}