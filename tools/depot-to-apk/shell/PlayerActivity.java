package dev.silksong.shell;

import android.app.Activity;
import android.content.Intent;
import android.content.res.Configuration;
import android.os.Bundle;
import android.view.KeyEvent;
import android.view.MotionEvent;

import com.unity3d.player.IUnityPlayerLifecycleEvents;
import com.unity3d.player.UnityPlayerForActivityOrService;

// Hosts the Unity player in an Activity.
//
// Unity ships an activity that does this, as a .java file in the Android
// player module, and the build used to compile it. That is Unity's code, and
// compiling it put it in our dex -- so the APK carried Unity-authored classes.
// The whole point of this port is that it ships nothing Unity-made: the engine,
// the player classes and the game are all fetched or built on the device from
// the user's own copy. So the activity is ours, written against the player's
// public API rather than derived from theirs.
//
// The API is small and entirely public: construct the player with a Context
// and a lifecycle-events callback, hand its FrameLayout to setContentView,
// and forward the Activity lifecycle to the matching player methods.
//
// Two details are not free choices:
//
//   - The field must be called mUnityPlayer. The engine's native side looks it
//     up on the activity by name, and Unity's own source carries a comment
//     saying as much. Renaming it compiles perfectly and fails at runtime.
//
//   - com.unity3d.player.* is not in this APK. These types resolve at runtime
//     from the dex the app builds on the device out of the player module it
//     downloads (see SilksongApp and UnityDex). They are on the compile
//     classpath here and nowhere in the shipped dex, which is exactly the
//     distinction that matters.
public class PlayerActivity extends Activity implements IUnityPlayerLifecycleEvents
{
    // Name fixed by the engine's native code. Do not rename.
    protected UnityPlayerForActivityOrService mUnityPlayer;

    @Override protected void onCreate(Bundle savedInstanceState)
    {
        super.onCreate(savedInstanceState);
        // Constructing the player is what starts the engine: it loads the
        // native libraries and brings up the render surface. Everything that
        // has to be in place first -- the staged libraries, the data package --
        // is done by GameActivity before it calls up into here.
        mUnityPlayer = new UnityPlayerForActivityOrService(this, this);
        setContentView(mUnityPlayer.getFrameLayout());
        mUnityPlayer.getFrameLayout().requestFocus();
    }

    @Override protected void onDestroy()
    {
        // Before super: the player tears down the native side, and the
        // Activity is still fully alive for that.
        if (mUnityPlayer != null) mUnityPlayer.destroy();
        super.onDestroy();
    }

    @Override protected void onStart()
    {
        super.onStart();
        if (mUnityPlayer != null) mUnityPlayer.onStart();
    }

    @Override protected void onStop()
    {
        super.onStop();
        if (mUnityPlayer != null) mUnityPlayer.onStop();
    }

    @Override protected void onResume()
    {
        super.onResume();
        if (mUnityPlayer != null) mUnityPlayer.onResume();
    }

    @Override protected void onPause()
    {
        super.onPause();
        if (mUnityPlayer != null) mUnityPlayer.onPause();
    }

    @Override protected void onNewIntent(Intent intent)
    {
        super.onNewIntent(intent);
        // The player reads the launch intent back off the activity, so this
        // has to be the current one before it is told about the change.
        setIntent(intent);
        if (mUnityPlayer != null) mUnityPlayer.newIntent(intent);
    }

    @Override public void onConfigurationChanged(Configuration newConfig)
    {
        super.onConfigurationChanged(newConfig);
        if (mUnityPlayer != null) mUnityPlayer.configurationChanged(newConfig);
    }

    @Override public void onWindowFocusChanged(boolean hasFocus)
    {
        super.onWindowFocusChanged(hasFocus);
        if (mUnityPlayer != null) mUnityPlayer.windowFocusChanged(hasFocus);
    }

    @Override public void onRequestPermissionsResult(
        int requestCode, String[] permissions, int[] grantResults)
    {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (mUnityPlayer != null)
            mUnityPlayer.permissionResponse(this, requestCode, permissions, grantResults);
    }

    // Input. The render surface receives most events on its own, but events
    // that arrive at the Activity instead have to be handed over explicitly --
    // gamepad buttons and sticks in particular, which is the difference
    // between a controller working and appearing dead.
    @Override public boolean onKeyDown(int keyCode, KeyEvent event)
    {
        return injected(event) || super.onKeyDown(keyCode, event);
    }

    @Override public boolean onKeyUp(int keyCode, KeyEvent event)
    {
        return injected(event) || super.onKeyUp(keyCode, event);
    }

    @Override public boolean onGenericMotionEvent(MotionEvent event)
    {
        return injected(event) || super.onGenericMotionEvent(event);
    }

    @Override public boolean onTouchEvent(MotionEvent event)
    {
        return injected(event) || super.onTouchEvent(event);
    }

    private boolean injected(android.view.InputEvent event)
    {
        return mUnityPlayer != null && mUnityPlayer.injectEvent(event);
    }

    // IUnityPlayerLifecycleEvents. The engine calls these when it unloads or
    // quits itself, which is not the same as the Activity being destroyed.
    @Override public void onUnityPlayerUnloaded()
    {
        // Unloaded but not finished: the process stays, so drop to the
        // background rather than leaving a blank window in front of the user.
        moveTaskToBack(true);
    }

    @Override public void onUnityPlayerQuitted()
    {
        // The engine has finished shutting down. Nothing to add: the process
        // is on its way out and the Activity teardown is already running.
    }
}
