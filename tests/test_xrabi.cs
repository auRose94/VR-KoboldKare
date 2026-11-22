// ABI regression tests: every XrNative struct size and structure-type constant
// is asserted against values compiled from the official openxr.h. Two incidents
// made this suite permanent: a four-constant drift (79/80/81 → 55/56/57) that
// surfaced as XR_ERROR_VALIDATION_FAILURE on xrAcquireSwapchainImage, and the
// OpenXR 1.1 XrFlags64 discovery (all flag fields are 64-bit in the 1.1 ABI).
// NEVER hand-copy a constant: extend this suite from refs/openxr.h instead.
//
// Pure C# (System.Runtime.InteropServices only) — runs in CI, no game needed.
// Suite "xrabi" via run_tests.sh.
using System;
using System.Runtime.InteropServices;
using VRKoboldKare;

internal static class TestXrAbi
{
    private static int _failed;

    private static void Eq(int expect, int got, string what)
    {
        if (expect != got)
        {
            _failed++;
            Console.WriteLine("FAIL " + what + ": expected " + expect + " got " + got);
        }
    }

    private static void EqU(uint expect, uint got, string what)
    {
        if (expect != got)
        {
            _failed++;
            Console.WriteLine("FAIL " + what + ": expected " + expect + " got " + got);
        }
    }

    private static void Size(Type t, int expect, string name)
    {
        int got = Marshal.SizeOf(t);
        if (got != expect)
        {
            _failed++;
            Console.WriteLine("FAIL sizeof " + name + ": expected " + expect + " got " + got);
        }
    }

    public static void Main()
    {
        // ---- struct sizes (values verified against openxr.h 1.1.65) ----
        Size(typeof(XrInstanceCreateInfo), 328, "XrInstanceCreateInfo");
        Size(typeof(XrInstanceProperties), 152, "XrInstanceProperties");
        Size(typeof(XrExtensionProperties), 152, "XrExtensionProperties");
        Size(typeof(XrSystemGetInfo), 24, "XrSystemGetInfo");
        Size(typeof(XrSystemProperties), 304, "XrSystemProperties");
        Size(typeof(XrSessionCreateInfo), 32, "XrSessionCreateInfo");
        Size(typeof(XrSessionBeginInfo), 24, "XrSessionBeginInfo");
        Size(typeof(XrGraphicsBindingD3D11KHR), 24, "XrGraphicsBindingD3D11KHR");
        Size(typeof(XrReferenceSpaceCreateInfo), 48, "XrReferenceSpaceCreateInfo");
        Size(typeof(XrActionSpaceCreateInfo), 64, "XrActionSpaceCreateInfo");
        Size(typeof(XrActionSetCreateInfo), 216, "XrActionSetCreateInfo");
        Size(typeof(XrActionCreateInfo), 224, "XrActionCreateInfo");
        Size(typeof(XrActionSuggestedBinding), 16, "XrActionSuggestedBinding");   // {action, binding} — no type header
        Size(typeof(XrInteractionProfileSuggestedBinding), 40, "XrInteractionProfileSuggestedBinding");
        Size(typeof(XrSessionActionSetsAttachInfo), 32, "XrSessionActionSetsAttachInfo");
        Size(typeof(XrActiveActionSet), 16, "XrActiveActionSet");
        Size(typeof(XrActionsSyncInfo), 32, "XrActionsSyncInfo");
        Size(typeof(XrActionStateGetInfo), 32, "XrActionStateGetInfo");
        Size(typeof(XrActionStateBoolean), 40, "XrActionStateBoolean");
        Size(typeof(XrActionStateFloat), 40, "XrActionStateFloat");
        Size(typeof(XrActionStateVector2f), 48, "XrActionStateVector2f");
        Size(typeof(XrActionStatePose), 24, "XrActionStatePose");
        Size(typeof(XrHapticActionInfo), 32, "XrHapticActionInfo");
        Size(typeof(XrHapticVibration), 32, "XrHapticVibration");
        Size(typeof(XrEventDataBuffer), 4016, "XrEventDataBuffer");
        Size(typeof(XrEventDataSessionStateChanged), 40, "XrEventDataSessionStateChanged");
        Size(typeof(XrFrameWaitInfo), 16, "XrFrameWaitInfo");
        Size(typeof(XrFrameState), 40, "XrFrameState");
        Size(typeof(XrFrameBeginInfo), 16, "XrFrameBeginInfo");
        Size(typeof(XrFrameEndInfo), 40, "XrFrameEndInfo");
        Size(typeof(XrViewLocateInfo), 40, "XrViewLocateInfo");
        Size(typeof(XrViewState), 24, "XrViewState");
        Size(typeof(XrView), 64, "XrView");
        Size(typeof(XrViewConfigurationView), 40, "XrViewConfigurationView");
        Size(typeof(XrSpaceLocation), 56, "XrSpaceLocation");
        Size(typeof(XrSwapchainCreateInfo), 64, "XrSwapchainCreateInfo");
        Size(typeof(XrSwapchainImageD3D11KHR), 24, "XrSwapchainImageD3D11KHR");
        Size(typeof(XrSwapchainSubImage), 32, "XrSwapchainSubImage");
        Size(typeof(XrCompositionLayerProjectionView), 96, "XrCompositionLayerProjectionView");
        Size(typeof(XrCompositionLayerProjection), 48, "XrCompositionLayerProjection");
        Size(typeof(XrHandTrackerCreateInfoEXT), 24, "XrHandTrackerCreateInfoEXT");
        Size(typeof(XrHandJointsLocateInfoEXT), 32, "XrHandJointsLocateInfoEXT");
        Size(typeof(XrHandJointLocationEXT), 40, "XrHandJointLocationEXT");
        Size(typeof(XrHandJointLocationsEXT), 32, "XrHandJointLocationsEXT");

        // ---- structure-type constants (the 55/56/57 + 53 incident) ----
        EqU(XrType.INSTANCE_CREATE_INFO, 3, "XR_TYPE_INSTANCE_CREATE_INFO");
        EqU(XrType.SYSTEM_GET_INFO, 4, "XR_TYPE_SYSTEM_GET_INFO");
        EqU(XrType.EXTENSION_PROPERTIES, 2, "XR_TYPE_EXTENSION_PROPERTIES");
        EqU(XrType.INSTANCE_PROPERTIES, 32, "XR_TYPE_INSTANCE_PROPERTIES");
        EqU(XrType.SYSTEM_PROPERTIES, 5, "XR_TYPE_SYSTEM_PROPERTIES");
        EqU(XrType.SESSION_CREATE_INFO, 8, "XR_TYPE_SESSION_CREATE_INFO");
        EqU(XrType.SESSION_BEGIN_INFO, 10, "XR_TYPE_SESSION_BEGIN_INFO");
        EqU(XrType.FRAME_END_INFO, 12, "XR_TYPE_FRAME_END_INFO");
        EqU(XrType.EVENT_DATA_BUFFER, 16, "XR_TYPE_EVENT_DATA_BUFFER");
        EqU(XrType.EVENT_DATA_SESSION_STATE_CHANGED, 18, "XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED");
        EqU(XrType.ACTION_SET_CREATE_INFO, 28, "XR_TYPE_ACTION_SET_CREATE_INFO");
        EqU(XrType.ACTION_CREATE_INFO, 29, "XR_TYPE_ACTION_CREATE_INFO");
        EqU(XrType.FRAME_STATE, 44, "XR_TYPE_FRAME_STATE");
        EqU(XrType.FRAME_BEGIN_INFO, 46, "XR_TYPE_FRAME_BEGIN_INFO");
        EqU(XrType.INTERACTION_PROFILE_SUGGESTED_BINDING, 51, "XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING");
        EqU(XrType.COMPOSITION_LAYER_PROJECTION, 35, "XR_TYPE_COMPOSITION_LAYER_PROJECTION");
        EqU(XrType.COMPOSITION_LAYER_PROJECTION_VIEW, 48, "XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW");
        EqU(XrType.REFERENCE_SPACE_CREATE_INFO, 37, "XR_TYPE_REFERENCE_SPACE_CREATE_INFO");
        EqU(XrType.ACTION_SPACE_CREATE_INFO, 38, "XR_TYPE_ACTION_SPACE_CREATE_INFO");
        EqU(XrType.VIEW_CONFIGURATION_VIEW, 41, "XR_TYPE_VIEW_CONFIGURATION_VIEW");
        EqU(XrType.SPACE_LOCATION, 42, "XR_TYPE_SPACE_LOCATION");
        EqU(XrType.ACTION_STATE_GET_INFO, 58, "XR_TYPE_ACTION_STATE_GET_INFO");
        EqU(XrType.HAPTIC_ACTION_INFO, 59, "XR_TYPE_HAPTIC_ACTION_INFO");
        EqU(XrType.SESSION_ACTION_SETS_ATTACH_INFO, 60, "XR_TYPE_SESSION_ACTION_SETS_ATTACH_INFO");
        EqU(XrType.ACTIONS_SYNC_INFO, 61, "XR_TYPE_ACTIONS_SYNC_INFO");
        EqU(XrType.VIEW_STATE, 11, "XR_TYPE_VIEW_STATE");
        EqU(XrType.VIEW, 7, "XR_TYPE_VIEW");
        EqU(XrType.INTERACTION_PROFILE_STATE, 53, "XR_TYPE_INTERACTION_PROFILE_STATE");
        EqU(XrType.SWAPCHAIN_CREATE_INFO, 9, "XR_TYPE_SWAPCHAIN_CREATE_INFO");
        EqU(XrType.SWAPCHAIN_IMAGE_ACQUIRE_INFO, 55, "XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO");
        EqU(XrType.SWAPCHAIN_IMAGE_WAIT_INFO, 56, "XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO");
        EqU(XrType.SWAPCHAIN_IMAGE_RELEASE_INFO, 57, "XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO");
        EqU(XrType.SWAPCHAIN_IMAGE_D3D11_KHR, 1000027001, "XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR");
        EqU(XrType.GRAPHICS_BINDING_D3D11_KHR, 1000027000, "XR_TYPE_GRAPHICS_BINDING_D3D11_KHR");
        EqU(XrType.HAND_TRACKER_CREATE_INFO_EXT, 1000051001, "XR_TYPE_HAND_TRACKER_CREATE_INFO_EXT");
        EqU(XrType.HAND_JOINTS_LOCATE_INFO_EXT, 1000051002, "XR_TYPE_HAND_JOINTS_LOCATE_INFO_EXT");
        EqU(XrType.HAND_JOINT_LOCATIONS_EXT, 1000051003, "XR_TYPE_HAND_JOINT_LOCATIONS_EXT");

        // ---- the 1.1 XrFlags64 discovery: flag fields must be 64-bit ----
        Eq(8, Marshal.SizeOf(typeof(ulong)), "XrFlags64 is 64-bit sanity");

        Console.WriteLine(_failed == 0 ? "xrabi: OK" : "xrabi: " + _failed + " FAILURES");
        if (_failed > 0) Environment.Exit(1);
    }
}
