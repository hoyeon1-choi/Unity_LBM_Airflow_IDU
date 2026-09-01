// FmuNativePlugin.cpp
// Build as x64 DLL
// Put output DLL into Assets/Plugins/x86_64/

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <pch.h>
#include <string>
#include <unordered_map>
#include <fstream>
#include <sstream>
#include <regex>
#include <memory>
#include <cstdio>
#include <cstdlib>
#include <cstdarg>

#define DLL_EXPORT extern "C" __declspec(dllexport)

typedef const char* fmi2String;
typedef double fmi2Real;
typedef int fmi2Integer;
typedef int fmi2Boolean;
typedef unsigned int fmi2ValueReference;
typedef void* fmi2Component;
typedef void* fmi2ComponentEnvironment;

enum fmi2Status
{
    fmi2OK = 0,
    fmi2Warning = 1,
    fmi2Discard = 2,
    fmi2Error = 3,
    fmi2Fatal = 4,
    fmi2Pending = 5
};

enum fmi2Type
{
    fmi2ModelExchange = 0,
    fmi2CoSimulation = 1
};

struct fmi2CallbackFunctions
{
    void (*logger)(fmi2ComponentEnvironment, fmi2String, fmi2Status, fmi2String, fmi2String, ...);
    void* (*allocateMemory)(size_t, size_t);
    void (*freeMemory)(void*);
    void (*stepFinished)(fmi2ComponentEnvironment, fmi2Status);
    fmi2ComponentEnvironment componentEnvironment;
};

typedef const char* (__cdecl* fmi2GetVersionTYPE)();
typedef const char* (__cdecl* fmi2GetTypesPlatformTYPE)();

typedef fmi2Component(__cdecl* fmi2InstantiateTYPE)(
    fmi2String instanceName,
    fmi2Type fmuType,
    fmi2String fmuGUID,
    fmi2String fmuResourceLocation,
    const fmi2CallbackFunctions* functions,
    fmi2Boolean visible,
    fmi2Boolean loggingOn);

typedef void(__cdecl* fmi2FreeInstanceTYPE)(fmi2Component c);

typedef fmi2Status(__cdecl* fmi2SetupExperimentTYPE)(
    fmi2Component c,
    fmi2Boolean toleranceDefined,
    fmi2Real tolerance,
    fmi2Real startTime,
    fmi2Boolean stopTimeDefined,
    fmi2Real stopTime);

typedef fmi2Status(__cdecl* fmi2EnterInitializationModeTYPE)(fmi2Component c);
typedef fmi2Status(__cdecl* fmi2ExitInitializationModeTYPE)(fmi2Component c);
typedef fmi2Status(__cdecl* fmi2TerminateTYPE)(fmi2Component c);
typedef fmi2Status(__cdecl* fmi2ResetTYPE)(fmi2Component c);

typedef fmi2Status(__cdecl* fmi2DoStepTYPE)(
    fmi2Component c,
    fmi2Real currentCommunicationPoint,
    fmi2Real communicationStepSize,
    fmi2Boolean noSetFMUStatePriorToCurrentPoint);

typedef fmi2Status(__cdecl* fmi2SetRealTYPE)(
    fmi2Component c,
    const fmi2ValueReference vr[],
    size_t nvr,
    const fmi2Real value[]);

typedef fmi2Status(__cdecl* fmi2GetRealTYPE)(
    fmi2Component c,
    const fmi2ValueReference vr[],
    size_t nvr,
    fmi2Real value[]);

struct FmuFunctions
{
    fmi2GetVersionTYPE fmi2GetVersion;
    fmi2GetTypesPlatformTYPE fmi2GetTypesPlatform;
    fmi2InstantiateTYPE fmi2Instantiate;
    fmi2FreeInstanceTYPE fmi2FreeInstance;
    fmi2SetupExperimentTYPE fmi2SetupExperiment;
    fmi2EnterInitializationModeTYPE fmi2EnterInitializationMode;
    fmi2ExitInitializationModeTYPE fmi2ExitInitializationMode;
    fmi2TerminateTYPE fmi2Terminate;
    fmi2ResetTYPE fmi2Reset;
    fmi2DoStepTYPE fmi2DoStep;
    fmi2SetRealTYPE fmi2SetReal;
    fmi2GetRealTYPE fmi2GetReal;

    FmuFunctions()
        : fmi2GetVersion(nullptr)
        , fmi2GetTypesPlatform(nullptr)
        , fmi2Instantiate(nullptr)
        , fmi2FreeInstance(nullptr)
        , fmi2SetupExperiment(nullptr)
        , fmi2EnterInitializationMode(nullptr)
        , fmi2ExitInitializationMode(nullptr)
        , fmi2Terminate(nullptr)
        , fmi2Reset(nullptr)
        , fmi2DoStep(nullptr)
        , fmi2SetReal(nullptr)
        , fmi2GetReal(nullptr)
    {
    }
};

static std::string g_lastError;
static std::string g_debugLogPath;

static void AppendLogLine(const std::string& msg)
{
    if (msg.empty())
    {
        return;
    }

    OutputDebugStringA((msg + "\n").c_str());

    if (!g_debugLogPath.empty())
    {
        std::ofstream ofs(g_debugLogPath.c_str(), std::ios::out | std::ios::app | std::ios::binary);
        if (ofs.is_open())
        {
            ofs << msg << "\n";
        }
    }
}

static const char* StatusName(fmi2Status status)
{
    switch (status)
    {
        case fmi2OK: return "fmi2OK";
        case fmi2Warning: return "fmi2Warning";
        case fmi2Discard: return "fmi2Discard";
        case fmi2Error: return "fmi2Error";
        case fmi2Fatal: return "fmi2Fatal";
        case fmi2Pending: return "fmi2Pending";
        default: return "unknown";
    }
}

static void SetLastErrorMsg(const std::string& msg)
{
    g_lastError = msg;
    if (!msg.empty())
    {
        AppendLogLine("[FmuNativePlugin][ERROR] " + msg);
    }
}

static void SetFmiStatusError(const std::string& operation, fmi2Status status)
{
    SetLastErrorMsg(operation + " returned " + std::to_string((int)status) + " (" + StatusName(status) + ")");
}

static void fmuLogger(
    fmi2ComponentEnvironment,
    fmi2String instanceName,
    fmi2Status status,
    fmi2String category,
    fmi2String message,
    ...)
{
    char formatted[2048];
    formatted[0] = '\0';

    if (message)
    {
        va_list args;
        va_start(args, message);
        std::vsnprintf(formatted, sizeof(formatted), message, args);
        va_end(args);
    }

    char buffer[4096];
    std::snprintf(
        buffer,
        sizeof(buffer),
        "[FMU][%s][%s:%d][%s] %s",
        instanceName ? instanceName : "unknown",
        StatusName(status),
        (int)status,
        category ? category : "no-category",
        formatted[0] != '\0' ? formatted : "no-message");
    AppendLogLine(buffer);
}

static std::string ReadTextFile(const std::string& path)
{
    std::ifstream ifs(path.c_str(), std::ios::in | std::ios::binary);
    if (!ifs.is_open())
    {
        return "";
    }

    std::ostringstream oss;
    oss << ifs.rdbuf();
    return oss.str();
}

static std::string ToFileUri(const std::string& path)
{
    std::string p = path;
    for (size_t i = 0; i < p.size(); ++i)
    {
        if (p[i] == '\\')
        {
            p[i] = '/';
        }
    }
    return "file:///" + p;
}

struct FmuVariableMap
{
    std::unordered_map<std::string, fmi2ValueReference> realVR;
};

struct FmuInstance
{
    std::string unzipDir;
    std::string modelIdentifier;
    std::string guid;
    std::string instanceName;

    HMODULE dllHandle;
    fmi2Component component;
    FmuFunctions fn;
    fmi2CallbackFunctions callbacks;
    FmuVariableMap vars;

    double startTime;
    double stopTime;
    bool hasStopTime;
    bool loggingOn;
    bool experimentSetup;
    bool inInitializationMode;
    bool initialized;

    std::unordered_map<std::string, double> initialRealValues;

    FmuInstance()
        : dllHandle(nullptr)
        , component(nullptr)
        , startTime(0.0)
        , stopTime(0.0)
        , hasStopTime(false)
        , loggingOn(true)
        , experimentSetup(false)
        , inInitializationMode(false)
        , initialized(false)
    {
        callbacks.logger = &fmuLogger;
        callbacks.allocateMemory = std::calloc;
        callbacks.freeMemory = std::free;
        callbacks.stepFinished = nullptr;
        callbacks.componentEnvironment = nullptr;
    }
};

static bool RegexExtractOne(const std::string& text, const std::regex& rgx, std::string& out1)
{
    std::smatch m;
    if (!std::regex_search(text, m, rgx))
    {
        return false;
    }

    if (m.size() < 2)
    {
        return false;
    }

    out1 = m[1].str();
    return true;
}

static bool ParseModelDescription(
    const std::string& xmlPath,
    std::string& outGuid,
    std::string& outModelIdentifier,
    FmuVariableMap& outVars)
{
    const std::string xml = ReadTextFile(xmlPath);
    if (xml.empty())
    {
        SetLastErrorMsg("Failed to read modelDescription.xml: " + xmlPath);
        return false;
    }

    {
        std::regex guidRgx("guid\\s*=\\s*\"([^\"]+)\"");
        if (!RegexExtractOne(xml, guidRgx, outGuid))
        {
            SetLastErrorMsg("Failed to parse guid from modelDescription.xml");
            return false;
        }
    }

    {
        std::regex csRgx("<CoSimulation[^>]*modelIdentifier\\s*=\\s*\"([^\"]+)\"");
        if (!RegexExtractOne(xml, csRgx, outModelIdentifier))
        {
            SetLastErrorMsg("Failed to parse CoSimulation modelIdentifier from modelDescription.xml");
            return false;
        }
    }

    std::regex scalarVarRgx(
        "<ScalarVariable[^>]*name\\s*=\\s*\"([^\"]+)\"[^>]*valueReference\\s*=\\s*\"([0-9]+)\"[^>]*>[\\s\\S]*?<Real\\b[^>]*/>",
        std::regex::icase);

    std::sregex_iterator it(xml.begin(), xml.end(), scalarVarRgx);
    std::sregex_iterator end;

    for (; it != end; ++it)
    {
        std::smatch m = *it;
        if (m.size() >= 3)
        {
            const std::string name = m[1].str();
            const fmi2ValueReference vr = static_cast<fmi2ValueReference>(std::stoul(m[2].str()));
            outVars.realVR[name] = vr;
        }
    }

    if (outVars.realVR.empty())
    {
        SetLastErrorMsg("No Real variables found in modelDescription.xml");
        return false;
    }

    return true;
}

static bool LoadFunction(HMODULE dll, const char* name, FARPROC& outProc)
{
    outProc = GetProcAddress(dll, name);
    if (!outProc)
    {
        SetLastErrorMsg(std::string("Failed to load FMI function: ") + name);
        return false;
    }
    return true;
}

static bool LoadFmiFunctions(FmuInstance& fmu)
{
    FARPROC p = nullptr;

    if (!LoadFunction(fmu.dllHandle, "fmi2GetVersion", p)) return false;
    fmu.fn.fmi2GetVersion = reinterpret_cast<fmi2GetVersionTYPE>(p);

    if (!LoadFunction(fmu.dllHandle, "fmi2GetTypesPlatform", p)) return false;
    fmu.fn.fmi2GetTypesPlatform = reinterpret_cast<fmi2GetTypesPlatformTYPE>(p);

    if (!LoadFunction(fmu.dllHandle, "fmi2Instantiate", p)) return false;
    fmu.fn.fmi2Instantiate = reinterpret_cast<fmi2InstantiateTYPE>(p);

    if (!LoadFunction(fmu.dllHandle, "fmi2FreeInstance", p)) return false;
    fmu.fn.fmi2FreeInstance = reinterpret_cast<fmi2FreeInstanceTYPE>(p);

    if (!LoadFunction(fmu.dllHandle, "fmi2SetupExperiment", p)) return false;
    fmu.fn.fmi2SetupExperiment = reinterpret_cast<fmi2SetupExperimentTYPE>(p);

    if (!LoadFunction(fmu.dllHandle, "fmi2EnterInitializationMode", p)) return false;
    fmu.fn.fmi2EnterInitializationMode = reinterpret_cast<fmi2EnterInitializationModeTYPE>(p);

    if (!LoadFunction(fmu.dllHandle, "fmi2ExitInitializationMode", p)) return false;
    fmu.fn.fmi2ExitInitializationMode = reinterpret_cast<fmi2ExitInitializationModeTYPE>(p);

    if (!LoadFunction(fmu.dllHandle, "fmi2Terminate", p)) return false;
    fmu.fn.fmi2Terminate = reinterpret_cast<fmi2TerminateTYPE>(p);

    if (!LoadFunction(fmu.dllHandle, "fmi2Reset", p)) return false;
    fmu.fn.fmi2Reset = reinterpret_cast<fmi2ResetTYPE>(p);

    if (!LoadFunction(fmu.dllHandle, "fmi2DoStep", p)) return false;
    fmu.fn.fmi2DoStep = reinterpret_cast<fmi2DoStepTYPE>(p);

    if (!LoadFunction(fmu.dllHandle, "fmi2SetReal", p)) return false;
    fmu.fn.fmi2SetReal = reinterpret_cast<fmi2SetRealTYPE>(p);

    if (!LoadFunction(fmu.dllHandle, "fmi2GetReal", p)) return false;
    fmu.fn.fmi2GetReal = reinterpret_cast<fmi2GetRealTYPE>(p);

    return true;
}

static bool SetRealByName(FmuInstance& fmu, const std::string& name, double value)
{
    std::unordered_map<std::string, fmi2ValueReference>::iterator it = fmu.vars.realVR.find(name);
    if (it == fmu.vars.realVR.end())
    {
        SetLastErrorMsg("SetReal failed. Variable not found: " + name);
        return false;
    }

    fmi2ValueReference vr = it->second;
    fmi2Real v = value;
    fmi2Status s = fmu.fn.fmi2SetReal(fmu.component, &vr, 1, &v);
    if (s > fmi2Warning)
    {
        SetFmiStatusError("fmi2SetReal(" + name + ")", s);
        return false;
    }

    return true;
}

static bool GetRealByName(FmuInstance& fmu, const std::string& name, double& value)
{
    std::unordered_map<std::string, fmi2ValueReference>::iterator it = fmu.vars.realVR.find(name);
    if (it == fmu.vars.realVR.end())
    {
        SetLastErrorMsg("GetReal failed. Variable not found: " + name);
        return false;
    }

    fmi2ValueReference vr = it->second;
    fmi2Real v = 0.0;
    fmi2Status s = fmu.fn.fmi2GetReal(fmu.component, &vr, 1, &v);
    if (s > fmi2Warning)
    {
        SetFmiStatusError("fmi2GetReal(" + name + ")", s);
        return false;
    }

    value = v;
    return true;
}

static bool ApplyInitialValues(FmuInstance& fmu)
{
    std::unordered_map<std::string, double>::const_iterator it = fmu.initialRealValues.begin();
    for (; it != fmu.initialRealValues.end(); ++it)
    {
        if (!SetRealByName(fmu, it->first, it->second))
        {
            return false;
        }
    }
    return true;
}

static bool SetupExperimentFmu(
    FmuInstance& fmu,
    double startTime,
    double stopTime,
    bool hasStopTime,
    double tolerance,
    bool toleranceDefined)
{
    fmu.startTime = startTime;
    fmu.stopTime = stopTime;
    fmu.hasStopTime = hasStopTime;

    fmi2Status s = fmu.fn.fmi2SetupExperiment(
        fmu.component,
        toleranceDefined ? 1 : 0,
        tolerance,
        fmu.startTime,
        fmu.hasStopTime ? 1 : 0,
        fmu.stopTime);

    if (s > fmi2Warning)
    {
        SetFmiStatusError("fmi2SetupExperiment", s);
        return false;
    }

    fmu.experimentSetup = true;
    return true;
}

static bool EnterInitializationModeFmu(FmuInstance& fmu)
{
    if (fmu.initialized || fmu.inInitializationMode)
    {
        return true;
    }

    if (!fmu.experimentSetup)
    {
        if (!SetupExperimentFmu(fmu, fmu.startTime, fmu.stopTime, fmu.hasStopTime, 0.0, false))
        {
            return false;
        }
    }

    fmi2Status s = fmu.fn.fmi2EnterInitializationMode(fmu.component);
    if (s > fmi2Warning)
    {
        SetFmiStatusError("fmi2EnterInitializationMode", s);
        return false;
    }

    fmu.inInitializationMode = true;
    return true;
}

static bool ExitInitializationModeFmu(FmuInstance& fmu)
{
    if (fmu.initialized)
    {
        return true;
    }

    if (!fmu.inInitializationMode)
    {
        SetLastErrorMsg("fmi2ExitInitializationMode requested before entering initialization mode");
        return false;
    }

    if (!ApplyInitialValues(fmu))
    {
        return false;
    }

    fmi2Status s = fmu.fn.fmi2ExitInitializationMode(fmu.component);
    if (s > fmi2Warning)
    {
        SetFmiStatusError("fmi2ExitInitializationMode", s);
        return false;
    }

    fmu.inInitializationMode = false;
    fmu.initialized = true;
    return true;
}

static bool InitializeFmu(FmuInstance& fmu)
{
    if (!SetupExperimentFmu(fmu, fmu.startTime, fmu.stopTime, fmu.hasStopTime, 0.0, false))
    {
        return false;
    }

    if (!EnterInitializationModeFmu(fmu))
    {
        return false;
    }

    return ExitInitializationModeFmu(fmu);
}

static void DestroyFmu(FmuInstance& fmu)
{
    if (fmu.component && fmu.initialized && fmu.fn.fmi2Terminate)
    {
        fmi2Status s = fmu.fn.fmi2Terminate(fmu.component);
        if (s > fmi2Warning)
        {
            AppendLogLine(std::string("[FmuNativePlugin][WARN] fmi2Terminate returned ") + StatusName(s));
        }
    }

    if (fmu.component && fmu.fn.fmi2FreeInstance)
    {
        fmu.fn.fmi2FreeInstance(fmu.component);
    }

    fmu.component = nullptr;
    fmu.initialized = false;
    fmu.inInitializationMode = false;
    fmu.experimentSetup = false;

    if (fmu.dllHandle)
    {
        FreeLibrary(fmu.dllHandle);
        fmu.dllHandle = nullptr;
    }
}

DLL_EXPORT const char* Fmu_GetLastError()
{
    return g_lastError.c_str();
}

DLL_EXPORT int Fmu_SetDebugLogPath(const char* path)
{
    g_debugLogPath = path ? path : "";
    return 1;
}

DLL_EXPORT void* Fmu_Load(const char* unzipDir, const char* instanceName, int loggingOn)
{
    if (!unzipDir || !instanceName)
    {
        SetLastErrorMsg("Fmu_Load: invalid argument");
        return nullptr;
    }

    g_lastError.clear();

    std::unique_ptr<FmuInstance> fmu(new FmuInstance());
    fmu->unzipDir = unzipDir;
    fmu->instanceName = instanceName;
    fmu->loggingOn = (loggingOn != 0);

    if (g_debugLogPath.empty())
    {
        g_debugLogPath = fmu->unzipDir + "\\FmuNativePlugin.log";
    }

    AppendLogLine("[FmuNativePlugin] Loading FMU instance=" + fmu->instanceName + " unzip=" + fmu->unzipDir);

    const std::string xmlPath = fmu->unzipDir + "\\modelDescription.xml";
    if (!ParseModelDescription(xmlPath, fmu->guid, fmu->modelIdentifier, fmu->vars))
    {
        return nullptr;
    }

    const std::string dllDir = fmu->unzipDir + "\\binaries\\win64";
    const std::string dllPath = dllDir + "\\" + fmu->modelIdentifier + ".dll";

    SetDllDirectoryA(dllDir.c_str());
    fmu->dllHandle = LoadLibraryExA(dllPath.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (!fmu->dllHandle)
    {
        DWORD errorCode = GetLastError();
        SetLastErrorMsg(
            "Failed to load FMU DLL: " + dllPath + " (Win32=" + std::to_string((unsigned long)errorCode) + ")");
        return nullptr;
    }

    if (!LoadFmiFunctions(*fmu))
    {
        DestroyFmu(*fmu);
        return nullptr;
    }

    const std::string resourcePath = fmu->unzipDir + "\\resources";
    const std::string resourceUri = ToFileUri(resourcePath);
    fmu->callbacks.componentEnvironment = fmu.get();

    fmu->component = fmu->fn.fmi2Instantiate(
        fmu->instanceName.c_str(),
        fmi2CoSimulation,
        fmu->guid.c_str(),
        resourceUri.c_str(),
        &fmu->callbacks,
        0,
        fmu->loggingOn ? 1 : 0);

    if (!fmu->component)
    {
        DestroyFmu(*fmu);
        SetLastErrorMsg("fmi2Instantiate failed");
        return nullptr;
    }

    return fmu.release();
}

DLL_EXPORT int Fmu_SetupExperiment(
    void* handle,
    double startTime,
    double stopTime,
    int hasStopTime,
    double tolerance,
    int toleranceDefined)
{
    if (!handle)
    {
        return 0;
    }

    FmuInstance* fmu = reinterpret_cast<FmuInstance*>(handle);
    return SetupExperimentFmu(
        *fmu,
        startTime,
        stopTime,
        hasStopTime != 0,
        tolerance,
        toleranceDefined != 0) ? 1 : 0;
}

DLL_EXPORT int Fmu_EnterInitializationMode(void* handle)
{
    if (!handle)
    {
        return 0;
    }

    FmuInstance* fmu = reinterpret_cast<FmuInstance*>(handle);
    return EnterInitializationModeFmu(*fmu) ? 1 : 0;
}

DLL_EXPORT int Fmu_ExitInitializationMode(void* handle)
{
    if (!handle)
    {
        return 0;
    }

    FmuInstance* fmu = reinterpret_cast<FmuInstance*>(handle);
    return ExitInitializationModeFmu(*fmu) ? 1 : 0;
}

DLL_EXPORT int Fmu_Initialize(void* handle, double startTime, double stopTime, int hasStopTime)
{
    if (!handle)
    {
        return 0;
    }

    FmuInstance* fmu = reinterpret_cast<FmuInstance*>(handle);
    fmu->startTime = startTime;
    fmu->stopTime = stopTime;
    fmu->hasStopTime = (hasStopTime != 0);

    return InitializeFmu(*fmu) ? 1 : 0;
}

DLL_EXPORT int Fmu_SetReal(void* handle, const char* varName, double value)
{
    if (!handle || !varName)
    {
        return 0;
    }

    FmuInstance* fmu = reinterpret_cast<FmuInstance*>(handle);
    return SetRealByName(*fmu, varName, value) ? 1 : 0;
}

DLL_EXPORT int Fmu_GetReal(void* handle, const char* varName, double* outValue)
{
    if (!handle || !varName || !outValue)
    {
        return 0;
    }

    FmuInstance* fmu = reinterpret_cast<FmuInstance*>(handle);
    double v = 0.0;
    if (!GetRealByName(*fmu, varName, v))
    {
        return 0;
    }

    *outValue = v;
    return 1;
}

DLL_EXPORT int Fmu_RegisterInitialReal(void* handle, const char* varName, double value)
{
    if (!handle || !varName)
    {
        return 0;
    }

    FmuInstance* fmu = reinterpret_cast<FmuInstance*>(handle);
    fmu->initialRealValues[varName] = value;
    return 1;
}

DLL_EXPORT int Fmu_DoStep(void* handle, double currentTime, double stepSize)
{
    if (!handle)
    {
        return 0;
    }

    FmuInstance* fmu = reinterpret_cast<FmuInstance*>(handle);
    fmi2Status s = fmu->fn.fmi2DoStep(fmu->component, currentTime, stepSize, 1);

    if (s > fmi2Warning)
    {
        SetFmiStatusError("fmi2DoStep", s);
        return 0;
    }

    return 1;
}

DLL_EXPORT int Fmu_Reset(void* handle)
{
    if (!handle)
    {
        return 0;
    }

    FmuInstance* fmu = reinterpret_cast<FmuInstance*>(handle);
    fmi2Status s = fmu->fn.fmi2Reset(fmu->component);

    if (s > fmi2Warning)
    {
        SetFmiStatusError("fmi2Reset", s);
        return 0;
    }

    fmu->experimentSetup = false;
    fmu->inInitializationMode = false;
    fmu->initialized = false;
    return InitializeFmu(*fmu) ? 1 : 0;
}

DLL_EXPORT void Fmu_Unload(void* handle)
{
    if (!handle)
    {
        return;
    }

    FmuInstance* fmu = reinterpret_cast<FmuInstance*>(handle);
    DestroyFmu(*fmu);
    delete fmu;
}