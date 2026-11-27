// VR-KoboldKare — ABI-exact OpenXR 1.0 P/Invoke layer.
// Struct layouts and constants verified against KhronosGroup/OpenXR-SDK
// include/openxr/openxr.h (release-1.1.65) + openxr_platform.h (D3D11 ext).
//
// The Windows openxr_loader.dll is shipped next to the plugin by build.sh.
// Under Proton the loader finds wineopenxr.dll via the prefix registry
// (HKLM\Software\Khronos\OpenXR\1\ActiveRuntime), which forwards to the
// native runtime selected by XR_RUNTIME_JSON (WiVRn: openxr_wivrn.json).
//
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VRKoboldKare
{
    // ---------- result codes ----------
    internal static class XrResultCode
    {
        public const int XR_SUCCESS = 0;
        public const int XR_TIMEOUT_EXPIRED = 1;
        public const int XR_SESSION_LOSS_PENDING = 3;
        public const int XR_EVENT_UNAVAILABLE = 4;
        public const int XR_SESSION_NOT_FOCUSED = 8;
        public const int XR_FRAME_DISCARDED = 9;
        public const int XR_ERROR_VALIDATION_FAILURE = -1;
        public const int XR_ERROR_RUNTIME_FAILURE = -2;
        public const int XR_ERROR_OUT_OF_MEMORY = -3;
        public const int XR_ERROR_API_VERSION_UNSUPPORTED = -4;
        public const int XR_ERROR_INITIALIZATION_FAILED = -6;
        public const int XR_ERROR_FUNCTION_UNSUPPORTED = -7;
        public const int XR_ERROR_FEATURE_UNSUPPORTED = -8;
        public const int XR_ERROR_EXTENSION_NOT_PRESENT = -9;
        public const int XR_ERROR_INSTANCE_LOST = -13;
        public const int XR_ERROR_SESSION_RUNNING = -14;
        public const int XR_ERROR_SESSION_NOT_RUNNING = -16;
        public const int XR_ERROR_INSTANCE_PERM_LOST = -15; // alias class
        public const int XR_ERROR_SESSION_LOST = -17;
        public const int XR_ERROR_SYSTEM_INVALID = -18;
        public const int XR_ERROR_PATH_INVALID = -19;
        public const int XR_ERROR_PATH_COUNT_EXCEEDED = -20;
        public const int XR_ERROR_PATH_FORMAT_INVALID = -21;
        public const int XR_ERROR_PATH_UNSUPPORTED = -22;
        public const int XR_ERROR_ACTION_TYPE_MISMATCH = -27;
        public const int XR_ERROR_RUNTIME_UNAVAILABLE = -51;
        public const int XR_ERROR_CALL_ORDER_INVALID = -37;
        public const int XR_ERROR_GRAPHICS_DEVICE_INVALID = -38;
        public const int XR_ERROR_FORM_FACTOR_UNSUPPORTED = -34;
        public const int XR_ERROR_FORM_FACTOR_UNAVAILABLE = -35;
        public const int XR_ERROR_REFERENCE_SPACE_UNSUPPORTED = -31;
        public const int XR_ERROR_ENVIRONMENT_BLEND_MODE_UNSUPPORTED = -42;
        public const int XR_ERROR_NAME_DUPLICATED = -44;
        public const int XR_ERROR_NAME_INVALID = -45;
        public const int XR_ERROR_ACTIONSETS_ALREADY_ATTACHED = -49;
        public const int XR_ERROR_SESSION_NOT_READY = -28;
        public const int XR_ERROR_SESSION_NOT_STOPPING = -29;

        public static bool Ok(int r) { return r >= 0; }

        public static string Name(int r)
        {
            switch (r)
            {
                case XR_SUCCESS: return "XR_SUCCESS";
                case XR_TIMEOUT_EXPIRED: return "XR_TIMEOUT_EXPIRED";
                case XR_SESSION_LOSS_PENDING: return "XR_SESSION_LOSS_PENDING";
                case XR_EVENT_UNAVAILABLE: return "XR_EVENT_UNAVAILABLE";
                case XR_SESSION_NOT_FOCUSED: return "XR_SESSION_NOT_FOCUSED";
                case XR_FRAME_DISCARDED: return "XR_FRAME_DISCARDED";
                case XR_ERROR_VALIDATION_FAILURE: return "XR_ERROR_VALIDATION_FAILURE";
                case XR_ERROR_RUNTIME_FAILURE: return "XR_ERROR_RUNTIME_FAILURE";
                case XR_ERROR_OUT_OF_MEMORY: return "XR_ERROR_OUT_OF_MEMORY";
                case XR_ERROR_API_VERSION_UNSUPPORTED: return "XR_ERROR_API_VERSION_UNSUPPORTED";
                case XR_ERROR_INITIALIZATION_FAILED: return "XR_ERROR_INITIALIZATION_FAILED";
                case XR_ERROR_FUNCTION_UNSUPPORTED: return "XR_ERROR_FUNCTION_UNSUPPORTED";
                case XR_ERROR_FEATURE_UNSUPPORTED: return "XR_ERROR_FEATURE_UNSUPPORTED";
                case XR_ERROR_EXTENSION_NOT_PRESENT: return "XR_ERROR_EXTENSION_NOT_PRESENT";
                case XR_ERROR_INSTANCE_LOST: return "XR_ERROR_INSTANCE_LOST";
                case XR_ERROR_SESSION_RUNNING: return "XR_ERROR_SESSION_RUNNING";
                case XR_ERROR_SESSION_NOT_RUNNING: return "XR_ERROR_SESSION_NOT_RUNNING";
                case XR_ERROR_SESSION_LOST: return "XR_ERROR_SESSION_LOST";
                case XR_ERROR_PATH_INVALID: return "XR_ERROR_PATH_INVALID";
                case XR_ERROR_PATH_UNSUPPORTED: return "XR_ERROR_PATH_UNSUPPORTED";
                case XR_ERROR_RUNTIME_UNAVAILABLE: return "XR_ERROR_RUNTIME_UNAVAILABLE";
                case XR_ERROR_CALL_ORDER_INVALID: return "XR_ERROR_CALL_ORDER_INVALID";
                case XR_ERROR_GRAPHICS_DEVICE_INVALID: return "XR_ERROR_GRAPHICS_DEVICE_INVALID";
                case XR_ERROR_FORM_FACTOR_UNSUPPORTED: return "XR_ERROR_FORM_FACTOR_UNSUPPORTED";
                case XR_ERROR_FORM_FACTOR_UNAVAILABLE: return "XR_ERROR_FORM_FACTOR_UNAVAILABLE";
                case XR_ERROR_REFERENCE_SPACE_UNSUPPORTED: return "XR_ERROR_REFERENCE_SPACE_UNSUPPORTED";
                case XR_ERROR_NAME_DUPLICATED: return "XR_ERROR_NAME_DUPLICATED";
                case XR_ERROR_NAME_INVALID: return "XR_ERROR_NAME_INVALID";
                default: return "XRResult(" + r + ")";
            }
        }
    }

    // ---------- structure types (verified against openxr.h) ----------
    internal static class XrType
    {
        public const uint UNKNOWN = 0;
        public const uint INSTANCE_CREATE_INFO = 3;
        public const uint SYSTEM_GET_INFO = 4;
        public const uint VIEW_LOCATE_INFO = 6;
        public const uint VIEW = 7;
        public const uint SESSION_CREATE_INFO = 8;
        public const uint SWAPCHAIN_CREATE_INFO = 9;
        public const uint SESSION_BEGIN_INFO = 10;
        public const uint VIEW_STATE = 11;
        public const uint FRAME_END_INFO = 12;
        public const uint HAPTIC_VIBRATION = 13;
        public const uint EVENT_DATA_BUFFER = 16;
        public const uint EVENT_DATA_INSTANCE_LOSS_PENDING = 17;
        public const uint EVENT_DATA_SESSION_STATE_CHANGED = 18;
        public const uint ACTION_STATE_BOOLEAN = 23;
        public const uint ACTION_STATE_FLOAT = 24;
        public const uint ACTION_STATE_VECTOR2F = 25;
        public const uint ACTION_STATE_POSE = 27;
        public const uint ACTION_SET_CREATE_INFO = 28;
        public const uint ACTION_CREATE_INFO = 29;
        public const uint FRAME_WAIT_INFO = 33;
        public const uint COMPOSITION_LAYER_PROJECTION = 35;
        public const uint REFERENCE_SPACE_CREATE_INFO = 37;
        public const uint ACTION_SPACE_CREATE_INFO = 38;
        public const uint EVENT_DATA_REFERENCE_SPACE_CHANGE_PENDING = 40;
        public const uint VIEW_CONFIGURATION_VIEW = 41;
        public const uint SPACE_LOCATION = 42;
        public const uint SPACE_VELOCITY = 43;
        public const uint COMPOSITION_LAYER_PROJECTION_VIEW = 48;
        public const uint INTERACTION_PROFILE_SUGGESTED_BINDING = 51;
        public const uint EVENT_DATA_INTERACTION_PROFILE_CHANGED = 52;
        public const uint ACTION_STATE_GET_INFO = 58;
        public const uint HAPTIC_ACTION_INFO = 59;
        public const uint SESSION_ACTION_SETS_ATTACH_INFO = 60;
        public const uint ACTIONS_SYNC_INFO = 61;
        public const uint INTERACTION_PROFILE_STATE = 53;
        public const uint SWAPCHAIN_IMAGE_ACQUIRE_INFO = 55;
        public const uint SWAPCHAIN_IMAGE_WAIT_INFO = 56;
        public const uint SWAPCHAIN_IMAGE_RELEASE_INFO = 57;
        public const uint INSTANCE_PROPERTIES = 32;
        public const uint SYSTEM_PROPERTIES = 5;
        public const uint EVENT_DATA_EVENTS_LOST = 49;
        public const uint EXTENSION_PROPERTIES = 2;
        public const uint FRAME_STATE = 44;
        public const uint FRAME_BEGIN_INFO = 46;
        public const uint GRAPHICS_BINDING_D3D11_KHR = 1000027000;
        public const uint SWAPCHAIN_IMAGE_D3D11_KHR = 1000027001;
        public const uint HAND_TRACKER_CREATE_INFO_EXT = 1000051001;
        public const uint HAND_JOINTS_LOCATE_INFO_EXT = 1000051002;
        public const uint HAND_JOINT_LOCATIONS_EXT = 1000051003;
        // XR_FB_body_tracking (values verified against openxr.h 2026-11-27)
        public const uint BODY_TRACKER_CREATE_INFO_FB = 1000076001;
        public const uint BODY_JOINTS_LOCATE_INFO_FB = 1000076002;
        public const uint SYSTEM_BODY_TRACKING_PROPERTIES_FB = 1000076004;
        public const uint BODY_JOINT_LOCATIONS_FB = 1000076005;
    }

    // ---------- enums ----------
    internal static class XrEnums
    {
        public const uint FORM_FACTOR_HEAD_MOUNTED_DISPLAY = 1;
        public const uint REFERENCE_SPACE_VIEW = 1;
        public const uint REFERENCE_SPACE_LOCAL = 2;
        public const uint REFERENCE_SPACE_STAGE = 3;
        public const uint VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO = 2;
        public const uint ENVIRONMENT_BLEND_MODE_OPAQUE = 1;
        public const uint ENVIRONMENT_BLEND_MODE_ADDITIVE = 2;
        public const uint ACTION_TYPE_BOOLEAN_INPUT = 1;
        public const uint ACTION_TYPE_FLOAT_INPUT = 2;
        public const uint ACTION_TYPE_VECTOR2F_INPUT = 3;
        public const uint ACTION_TYPE_POSE_INPUT = 4;
        public const uint ACTION_TYPE_VIBRATION_OUTPUT = 100;
        // session states
        public const uint SESSION_STATE_IDLE = 1;
        public const uint SESSION_STATE_READY = 2;
        public const uint SESSION_STATE_SYNCHRONIZED = 3;
        public const uint SESSION_STATE_VISIBLE = 4;
        public const uint SESSION_STATE_FOCUSED = 5;
        public const uint SESSION_STATE_STOPPING = 6;
        public const uint SESSION_STATE_LOSS_PENDING = 7;
        public const uint SESSION_STATE_EXITING = 8;
        // space location flags
        public const uint SPACE_LOCATION_ORIENTATION_VALID_BIT = 0x00000001;
        public const uint SPACE_LOCATION_POSITION_VALID_BIT = 0x00000002;
        public const uint SPACE_LOCATION_ORIENTATION_TRACKED_BIT = 0x00000004;
        public const uint SPACE_LOCATION_POSITION_TRACKED_BIT = 0x00000008;
        // view state flags
        public const uint VIEW_STATE_ORIENTATION_VALID_BIT = 0x00000001;
        public const uint VIEW_STATE_POSITION_VALID_BIT = 0x00000002;
        // swapchain usage
        public const uint SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT = 0x00000001;
        public const uint SWAPCHAIN_USAGE_SAMPLED_BIT = 0x00000020;
        // composition layer flags
        public const uint COMPOSITION_LAYER_CORRECT_CHROMATIC_ABERRATION_BIT = 0x00000001;
        // misc
        public const ulong NULL_SYSTEM_ID = 0;
        public const ulong NULL_PATH = 0;
        public const long NO_DURATION = 0;
        public const long INFINITE_DURATION = long.MaxValue;
        // XR_EXT_hand_tracking (values verified against openxr.h)
        public const uint HAND_LEFT_EXT = 1;
        public const uint HAND_RIGHT_EXT = 2;
        public const uint HAND_JOINT_COUNT_EXT = 26;
        public const uint HAND_JOINT_PALM_EXT = 0;
        public const uint HAND_JOINT_THUMB_TIP_EXT = 5;
        public const uint HAND_JOINT_INDEX_TIP_EXT = 10;
        public const uint HAND_JOINT_MIDDLE_TIP_EXT = 15;
        // XR_FB_body_tracking (values verified against openxr.h 2026-11-27)
        public const uint TYPE_BODY_TRACKER_CREATE_INFO_FB = 1000076001;
        public const uint TYPE_BODY_JOINTS_LOCATE_INFO_FB = 1000076002;
        public const uint TYPE_SYSTEM_BODY_TRACKING_PROPERTIES_FB = 1000076004;
        public const uint TYPE_BODY_JOINT_LOCATIONS_FB = 1000076005;
        public const int BODY_JOINT_HIPS_FB = 1;
        public const int BODY_JOINT_SPINE_LOWER_FB = 2;
        public const int BODY_JOINT_SPINE_MIDDLE_FB = 3;
        public const int BODY_JOINT_SPINE_UPPER_FB = 4;
        public const int BODY_JOINT_CHEST_FB = 5;
        public const int BODY_JOINT_HEAD_FB = 7;
        public const int BODY_JOINT_LEFT_HAND_WRIST_FB = 19;
        public const int BODY_JOINT_RIGHT_HAND_WRIST_FB = 45;
        public const uint BODY_JOINT_COUNT_FB = 70;
        public const uint BODY_JOINT_SET_DEFAULT_FB = 0;   // upper body: no knees/feet
    }

    internal static class XrExt
    {
        // Official Khronos spelling — capital D3D. Extension names are
        // case-sensitive end to end.
        public const string D3D11 = "XR_KHR_D3D11_enable";
        public const string HandTracking = "XR_EXT_hand_tracking";
        public const string BodyTrackingFB = "XR_FB_body_tracking";
    }

    // ---------- primitives ----------
    // XrBool32 is uint32; XrTime/XrDuration are int64; handles are pointer-sized
    // opaque values (ulong on x64). XrPath/XrAction etc. are u64.

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrVector2f { public float x; public float y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrVector3f { public float x; public float y; public float z; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrQuaternionf { public float x; public float y; public float z; public float w; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrPosef
    {
        public XrQuaternionf orientation;
        public XrVector3f position;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrFovf
    {
        public float angleLeft;
        public float angleRight;
        public float angleUp;
        public float angleDown;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrOffset2Di { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrExtent2Di { public int width; public int height; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrRect2Di { public XrOffset2Di offset; public XrExtent2Di extent; }

    // ---------- info structs ----------

    // NOTE: built against the OpenXR 1.1 ABI (XrFlags64 — flag fields are
    // 64-bit), matching the openxr_loader.dll 1.1.65 we ship. All flag-typed
    // members below are ulong on purpose; sizes verified against the header.

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct XrInstanceCreateInfo
    {
        public uint type;
        public IntPtr next;
        public ulong createFlags;
        // XrApplicationInfo (inlined)
        public fixed byte applicationName[128];
        public uint applicationVersion;
        public fixed byte engineName[128];
        public uint engineVersion;
        public ulong apiVersion;      // XR_API_VERSION_1_0 = (1ull<<48)
        public uint enabledApiLayerCount;
        public IntPtr enabledApiLayerNames;
        public uint enabledExtensionCount;
        public IntPtr enabledExtensionNames;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct XrInstanceProperties
    {
        public uint type;
        public IntPtr next;
        public ulong runtimeVersion;
        public fixed byte runtimeName[128];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrSystemGetInfo
    {
        public uint type;
        public IntPtr next;
        public uint formFactor;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct XrSystemProperties
    {
        public uint type;
        public IntPtr next;
        public ulong systemId;
        public uint vendorId;
        public fixed byte systemName[256];
        public XrSystemGraphicsProperties graphicsProperties;
        public XrSystemTrackingProperties trackingProperties;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrSystemGraphicsProperties
    {
        public uint maxSwapchainImageWidth;
        public uint maxSwapchainImageHeight;
        public uint maxLayerCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrSystemTrackingProperties
    {
        public int orientationTracking;   // XrBool32
        public int positionTracking;      // XrBool32
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrSessionCreateInfo
    {
        public uint type;
        public IntPtr next;   // -> XrGraphicsBindingD3D11KHR when rendering
        public ulong createFlags;
        public ulong systemId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrGraphicsBindingD3D11KHR
    {
        public uint type;
        public IntPtr next;
        public IntPtr device;   // ID3D11Device*
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrReferenceSpaceCreateInfo
    {
        public uint type;
        public IntPtr next;
        public uint referenceSpaceType;
        public XrPosef poseInReferenceSpace;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrSessionBeginInfo
    {
        public uint type;
        public IntPtr next;
        public uint primaryViewConfigurationType;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct XrActionSetCreateInfo
    {
        public uint type;
        public IntPtr next;
        public fixed byte actionSetName[64];
        public fixed byte localizedActionSetName[128];
        public uint priority;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct XrActionCreateInfo
    {
        public uint type;
        public IntPtr next;
        public fixed byte actionName[64];
        public uint actionType;
        public uint countSubactionPaths;
        public IntPtr subactionPaths;   // const XrPath*
        public fixed byte localizedActionName[128];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrActionSuggestedBinding
    {
        public ulong action;
        public ulong binding;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrInteractionProfileSuggestedBinding
    {
        public uint type;
        public IntPtr next;
        public ulong interactionProfile;
        public uint countSuggestedBindings;
        public IntPtr suggestedBindings;   // const XrActionSuggestedBinding*
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrSessionActionSetsAttachInfo
    {
        public uint type;
        public IntPtr next;
        public uint countActionSets;
        public IntPtr actionSets;   // const XrActionSet*
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrActiveActionSet
    {
        public ulong actionSet;
        public ulong subactionPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrActionsSyncInfo
    {
        public uint type;
        public IntPtr next;
        public uint countActiveActionSets;
        public IntPtr activeActionSets;   // const XrActiveActionSet*
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrActionStateGetInfo
    {
        public uint type;
        public IntPtr next;
        public ulong action;
        public ulong subactionPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrActionStateBoolean
    {
        public uint type;
        public IntPtr next;
        public int currentState;          // XrBool32
        public int changedSinceLastSync;  // XrBool32
        public long lastChangeTime;
        public int isActive;              // XrBool32
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrActionStateFloat
    {
        public uint type;
        public IntPtr next;
        public float currentState;
        public int changedSinceLastSync;
        public long lastChangeTime;
        public int isActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrActionStateVector2f
    {
        public uint type;
        public IntPtr next;
        public XrVector2f currentState;
        public int changedSinceLastSync;
        public long lastChangeTime;
        public int isActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrActionStatePose
    {
        public uint type;
        public IntPtr next;
        public int isActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrActionSpaceCreateInfo
    {
        public uint type;
        public IntPtr next;
        public ulong action;
        public ulong subactionPath;
        public XrPosef poseInActionSpace;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrSpaceLocation
    {
        public uint type;
        public IntPtr next;
        public ulong locationFlags;
        public XrPosef pose;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrHapticActionInfo
    {
        public uint type;
        public IntPtr next;
        public ulong action;
        public ulong subactionPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrHapticVibration
    {
        public uint type;
        public IntPtr next;
        public long duration;   // XrDuration (ns); XR_MIN_HAPTIC_DURATION = 0
        public float frequency; // Hz; XR_FREQUENCY_UNSPECIFIED = 0
        public float amplitude; // 0..1
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrFrameWaitInfo { public uint type; public IntPtr next; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrFrameState
    {
        public uint type;
        public IntPtr next;
        public long predictedDisplayTime;
        public long predictedDisplayPeriod;
        public int shouldRender;   // XrBool32
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrFrameBeginInfo { public uint type; public IntPtr next; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrFrameEndInfo
    {
        public uint type;
        public IntPtr next;
        public long displayTime;
        public uint environmentBlendMode;
        public uint layerCount;
        public IntPtr layers;   // const XrCompositionLayerBaseHeader* const*
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrViewLocateInfo
    {
        public uint type;
        public IntPtr next;
        public uint viewConfigurationType;
        public long displayTime;
        public ulong space;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrViewState
    {
        public uint type;
        public IntPtr next;
        public ulong viewStateFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrView
    {
        public uint type;
        public IntPtr next;
        public XrPosef pose;
        public XrFovf fov;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrViewConfigurationView
    {
        public uint type;
        public IntPtr next;
        public uint recommendedImageRectWidth;
        public uint maxImageRectWidth;
        public uint recommendedImageRectHeight;
        public uint maxImageRectHeight;
        public uint recommendedSwapchainSampleCount;
        public uint maxSwapchainSampleCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrSwapchainCreateInfo
    {
        public uint type;
        public IntPtr next;
        public ulong createFlags;
        public ulong usageFlags;
        public long format;      // int64 (DXGI_FORMAT under D3D11)
        public uint sampleCount;
        public uint width;
        public uint height;
        public uint faceCount;
        public uint arraySize;
        public uint mipCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrSwapchainImageD3D11KHR
    {
        public uint type;
        public IntPtr next;
        public IntPtr texture;   // ID3D11Texture2D*
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrSwapchainSubImage
    {
        public ulong swapchain;
        public XrRect2Di imageRect;
        public uint imageArrayIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrCompositionLayerProjectionView
    {
        public uint type;
        public IntPtr next;
        public XrPosef pose;
        public XrFovf fov;
        public XrSwapchainSubImage subImage;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrCompositionLayerProjection
    {
        public uint type;
        public IntPtr next;
        public ulong layerFlags;
        public ulong space;
        public uint viewCount;
        public IntPtr views;   // const XrCompositionLayerProjectionView*
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrSwapchainImageAcquireInfo { public uint type; public IntPtr next; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrSwapchainImageWaitInfo
    {
        public uint type;
        public IntPtr next;
        public long timeout;   // XrDuration
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrSwapchainImageReleaseInfo { public uint type; public IntPtr next; }

    // ---------- XR_EXT_hand_tracking ----------

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrHandTrackerCreateInfoEXT
    {
        public uint type;
        public IntPtr next;
        public uint hand;          // XrHandEXT: 1 = left, 2 = right
        public uint handJointSet;  // XR_HAND_JOINT_SET_DEFAULT_EXT = 0
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrHandJointsLocateInfoEXT
    {
        public uint type;
        public IntPtr next;
        public ulong baseSpace;
        public long time;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrHandJointLocationEXT
    {
        public ulong locationFlags;   // XrSpaceLocationFlags (64-bit, 1.1 ABI)
        public XrPosef pose;
        public float radius;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrHandJointLocationsEXT
    {
        public uint type;
        public IntPtr next;
        public uint jointCount;
        public IntPtr joints;   // XrHandJointLocationEXT*
    }

    // ---------- XR_FB_body_tracking ----------
    // Struct sizes verified with gcc against refs/openxr.h (2026-11-27):
    //   BodyJointLocationFB=40 BodyTrackerCreateInfoFB=24
    //   BodyJointsLocateInfoFB=32 BodyJointLocationsFB=56
    //   SystemBodyTrackingPropertiesFB=24
    // NOTE: the DEFAULT joint set is UPPER BODY ONLY (70 joints: hips →
    // spine → chest → head → arms/hands) — no knees/feet. Legs stay
    // procedural in VRBody. Functions resolved via xrGetInstanceProcAddr
    // (the Khronos loader exports core functions only — see hand tracking).

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrBodyJointLocationFB
    {
        public ulong locationFlags;   // XrSpaceLocationFlags (64-bit, 1.1 ABI)
        public XrPosef pose;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrSystemBodyTrackingPropertiesFB
    {
        public uint type;
        public IntPtr next;
        public uint supportsBodyTracking;   // XrBool32
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrBodyTrackerCreateInfoFB
    {
        public uint type;
        public IntPtr next;
        public uint bodyJointSet;   // XrBodyJointSetFB
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrBodyJointsLocateInfoFB
    {
        public uint type;
        public IntPtr next;
        public ulong baseSpace;
        public long time;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrBodyJointLocationsFB
    {
        public uint type;
        public IntPtr next;
        public uint isActive;               // XrBool32
        public float confidence;
        public uint jointCount;
        public IntPtr jointLocations;       // XrBodyJointLocationFB*
        public uint skeletonChangedCount;
        public long time;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrInteractionProfileState
    {
        public uint type;
        public IntPtr next;
        public ulong interactionProfile;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct XrExtensionProperties
    {
        public uint type;
        public IntPtr next;
        public fixed byte extensionName[128];
        public uint extensionVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct XrEventDataBuffer
    {
        public uint type;
        public IntPtr next;
        public fixed byte varying[4000];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XrEventDataSessionStateChanged
    {
        public uint type;
        public IntPtr next;
        public ulong session;
        public uint state;
        public long time;
    }

    // ---------- string helpers ----------
    internal static unsafe class XrStr
    {
        public static void Copy(byte* dst, string s, int max)
        {
            byte[] b = Encoding.UTF8.GetBytes(s ?? "");
            int n = Math.Min(b.Length, max - 1);
            for (int i = 0; i < n; i++) dst[i] = b[i];
            dst[n] = 0;
        }

        public static string Read(byte* src, int max)
        {
            if (src == null) return "";
            int n = 0;
            while (n < max && src[n] != 0) n++;
            return Encoding.UTF8.GetString(src, n);
        }
    }

    // ---------- native entry points ----------
    internal static class XrApi
    {
        private const string Lib = "openxr_loader.dll";
        private const CallingConvention CC = CallingConvention.Winapi; // XRAPI_CALL = stdcall on Windows

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrEnumerateInstanceExtensionProperties(
            string layerName, uint propertyCapacityInput, out uint propertyCountOutput,
            [In, Out] XrExtensionProperties[] properties);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrCreateInstance(
            ref XrInstanceCreateInfo createInfo, out ulong instance);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrDestroyInstance(ulong instance);

        /// <summary>The spec-sanctioned way to obtain EXTENSION function
        /// pointers — loaders are not required to export them (our 1.1.65
        /// NuGet loader exports only the 56 core functions).</summary>
        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrGetInstanceProcAddr(
            ulong instance, [MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr proc);

        [DllImport(Lib, CallingConvention = CC)]
        public unsafe static extern int xrGetInstanceProperties(
            ulong instance, ref XrInstanceProperties properties);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrGetSystem(
            ulong instance, ref XrSystemGetInfo getInfo, out ulong systemId);

        [DllImport(Lib, CallingConvention = CC)]
        public unsafe static extern int xrGetSystemProperties(
            ulong instance, ulong systemId, ref XrSystemProperties properties);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrEnumerateViewConfigurationViews(
            ulong instance, ulong systemId, uint viewConfigurationType,
            uint capacityInput, out uint countOutput,
            [In, Out] XrViewConfigurationView[] views);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrEnumerateEnvironmentBlendModes(
            ulong instance, ulong systemId, uint viewConfigurationType,
            uint capacityInput, out uint countOutput, [Out] uint[] blendModes);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrCreateSession(
            ulong instance, ref XrSessionCreateInfo createInfo, out ulong session);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrDestroySession(ulong session);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrBeginSession(ulong session, ref XrSessionBeginInfo beginInfo);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrEndSession(ulong session);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrRequestExitSession(ulong session);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrCreateReferenceSpace(
            ulong session, ref XrReferenceSpaceCreateInfo createInfo, out ulong space);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrCreateActionSpace(
            ulong session, ref XrActionSpaceCreateInfo createInfo, out ulong space);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrDestroySpace(ulong space);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrLocateSpace(
            ulong space, ulong baseSpace, long time, ref XrSpaceLocation location);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrCreateActionSet(
            ulong instance, ref XrActionSetCreateInfo createInfo, out ulong actionSet);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrDestroyActionSet(ulong actionSet);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrCreateAction(
            ulong actionSet, ref XrActionCreateInfo createInfo, out ulong action);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrDestroyAction(ulong action);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrSuggestInteractionProfileBindings(
            ulong instance, ref XrInteractionProfileSuggestedBinding suggestedBindings);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrAttachSessionActionSets(
            ulong session, ref XrSessionActionSetsAttachInfo attachInfo);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrGetCurrentInteractionProfile(
            ulong session, ulong topLevelUserPath, ref XrInteractionProfileState state);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrSyncActions(
            ulong session, ref XrActionsSyncInfo syncInfo);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrGetActionStateBoolean(
            ulong session, ref XrActionStateGetInfo getInfo, ref XrActionStateBoolean state);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrGetActionStateFloat(
            ulong session, ref XrActionStateGetInfo getInfo, ref XrActionStateFloat state);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrGetActionStateVector2f(
            ulong session, ref XrActionStateGetInfo getInfo, ref XrActionStateVector2f state);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrGetActionStatePose(
            ulong session, ref XrActionStateGetInfo getInfo, ref XrActionStatePose state);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrApplyHapticFeedback(
            ulong session, ref XrHapticActionInfo hapticActionInfo, ref XrHapticVibration vibration);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrStopHapticFeedback(
            ulong session, ref XrHapticActionInfo hapticActionInfo);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrStringToPath(
            ulong instance, string pathString, out ulong path);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrPollEvent(ulong instance, ref XrEventDataBuffer eventData);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrWaitFrame(
            ulong session, ref XrFrameWaitInfo frameWaitInfo, ref XrFrameState frameState);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrBeginFrame(ulong session, ref XrFrameBeginInfo frameBeginInfo);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrEndFrame(ulong session, ref XrFrameEndInfo frameEndInfo);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrLocateViews(
            ulong session, ref XrViewLocateInfo viewLocateInfo, ref XrViewState viewState,
            uint viewCapacityInput, out uint viewCountOutput, [In, Out] XrView[] views);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrCreateSwapchain(
            ulong session, ref XrSwapchainCreateInfo createInfo, out ulong swapchain);

        [DllImport(Lib, CallingConvention = CC)]
        public unsafe static extern int xrEnumerateSwapchainFormats(
            ulong session, uint capacityInput, out uint countOutput, long* formats);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrDestroySwapchain(ulong swapchain);

        [DllImport(Lib, CallingConvention = CC)]
        public unsafe static extern int xrEnumerateSwapchainImages(
            ulong swapchain, uint capacityInput, out uint countOutput,
            XrSwapchainImageD3D11KHR* images);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrAcquireSwapchainImage(
            ulong swapchain, ref XrSwapchainImageAcquireInfo acquireInfo, out uint index);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrWaitSwapchainImage(
            ulong swapchain, ref XrSwapchainImageWaitInfo waitInfo);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrReleaseSwapchainImage(
            ulong swapchain, ref XrSwapchainImageReleaseInfo releaseInfo);

        // ---------- XR_EXT_hand_tracking ----------
        // NOTE: resolved via xrGetInstanceProcAddr at runtime — the Khronos
        // loader does NOT export extension functions (confirmed: 56 core
        // exports only). A DllImport here throws EntryPointNotFoundException.

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate int CreateHandTrackerEXT_t(
            ulong session, ref XrHandTrackerCreateInfoEXT createInfo, out ulong handTracker);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate int LocateHandJointsEXT_t(
            ulong handTracker, ref XrHandJointsLocateInfoEXT locateInfo,
            ref XrHandJointLocationsEXT locations);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate int DestroyHandTrackerEXT_t(ulong handTracker);

        // ---------- XR_FB_body_tracking ----------
        // Resolved via xrGetInstanceProcAddr (see the hand-tracking note —
        // the Khronos loader does not export extension functions).

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate int CreateBodyTrackerFB_t(
            ulong session, ref XrBodyTrackerCreateInfoFB createInfo, out ulong bodyTracker);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate int LocateBodyJointsFB_t(
            ulong bodyTracker, ref XrBodyJointsLocateInfoFB locateInfo,
            ref XrBodyJointLocationsFB locations);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate int DestroyBodyTrackerFB_t(ulong bodyTracker);

        [DllImport(Lib, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern int xrResultToString(ulong instance, int value, StringBuilder buffer);

        [DllImport(Lib, CallingConvention = CC)]
        public static extern int xrPathToString(
            ulong instance, ulong path, uint bufferCapacityInput,
            out uint bufferCountOutput, StringBuilder buffer);
    }

    // ---------- minimal COM vtable helper (D3D11 interop) ----------
    internal static class ComInterop
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void GetDeviceFn(IntPtr self, out IntPtr device);

        /// <summary>
        /// ID3D11DeviceChild::GetDevice is vtable slot 3 (after IUnknown).
        /// Works on any D3D11 resource (textures, views, ...).
        /// </summary>
        public static IntPtr GetD3D11DeviceFromResource(IntPtr comObject)
        {
            if (comObject == IntPtr.Zero) return IntPtr.Zero;
            try
            {
                IntPtr vtable = Marshal.ReadIntPtr(comObject);
                IntPtr fn = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
                var getDevice = (GetDeviceFn)Marshal.GetDelegateForFunctionPointer(fn, typeof(GetDeviceFn));
                IntPtr dev;
                getDevice(comObject, out dev);
                return dev;
            }
            catch { return IntPtr.Zero; }
        }
    }
}
