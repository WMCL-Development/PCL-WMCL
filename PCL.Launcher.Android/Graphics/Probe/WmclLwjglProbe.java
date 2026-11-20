import org.lwjgl.glfw.GLFW;
import org.lwjgl.glfw.GLFWCursorPosCallback;
import org.lwjgl.glfw.GLFWFramebufferSizeCallback;
import org.lwjgl.glfw.GLFWMouseButtonCallback;
import org.lwjgl.system.JNI;
import org.lwjgl.system.MemoryUtil;

/**
 * Runs on the HotSpot main thread. It uses the unmodified LWJGL GLFW bindings, exactly
 * like Minecraft's window code, but calls Android GLES through the host's getProcAddress.
 */
public final class WmclLwjglProbe {
    private static final int COLOR_BUFFER_BIT = 0x4000;
    private static final int SCISSOR_TEST = 0x0c11;
    private static final int GL_VERSION = 0x1f02;

    private static int touches;

    public static void main(String[] args) throws InterruptedException {
        if (!GLFW.glfwInit()) throw new IllegalStateException("glfwInit failed");
        long window = GLFW.glfwCreateWindow(1280, 720, "WMCL GLFW", 0, 0);
        if (window == 0) throw new IllegalStateException("glfwCreateWindow failed");
        try {
            GLFW.glfwMakeContextCurrent(window);
            if (GLFW.glfwGetCurrentContext() != window) throw new IllegalStateException("no current context");
            GLFW.glfwSwapInterval(0);

            long clearColor = require("glClearColor");
            long clear = require("glClear");
            long viewport = require("glViewport");
            long scissor = require("glScissor");
            long enable = require("glEnable");
            long disable = require("glDisable");
            long getError = require("glGetError");
            long getString = require("glGetString");

            long tick = 0;

            try (GLFWCursorPosCallback motion = GLFWCursorPosCallback.create(
                     (w, x, y) -> { if (w == window) { cursorX = x; cursorY = y; } });
                 GLFWMouseButtonCallback mouse = GLFWMouseButtonCallback.create((w, button, action, mods) -> {
                     if (w == window && button == GLFW.GLFW_MOUSE_BUTTON_LEFT) touches += action == GLFW.GLFW_PRESS ? 1 : 0;
                 });
                 GLFWFramebufferSizeCallback resize = GLFWFramebufferSizeCallback.create(
                     (w, width, height) -> { if (w == window) { frameWidth = width; frameHeight = height; } })) {
                GLFW.glfwSetCursorPosCallback(window, motion);
                GLFW.glfwSetMouseButtonCallback(window, mouse);
                GLFW.glfwSetFramebufferSizeCallback(window, resize);

                String version = MemoryUtil.memASCIISafe(JNI.callP(GL_VERSION, getString));
                if (version == null || !version.startsWith("OpenGL ES 3."))
                    throw new IllegalStateException("Expected GLES 3, got " + version);

                int lastTouches = -1;
                while (!GLFW.glfwWindowShouldClose(window)) {
                    GLFW.glfwPollEvents();
                    // The host may present at a different size than the requested window,
                    // and callbacks only fire on later changes: query it like Minecraft does.
                    GLFW.glfwGetFramebufferSize(window, sizeWidth, sizeHeight);
                    frameWidth = Math.max(1, sizeWidth[0]);
                    frameHeight = Math.max(1, sizeHeight[0]);
                    JNI.callV(0, 0, frameWidth, frameHeight, viewport);
                    boolean lit = (touches & 1) != 0;
                    JNI.callV(lit ? 0.56f : 0.09f, lit ? 0.20f : 0.34f, lit ? 0.12f : 0.65f, 1f, clearColor);
                    JNI.callV(COLOR_BUFFER_BIT, clear);
                    if (touches != 0) {
                        int box = Math.max(1, Math.min(frameWidth, frameHeight) / 9);
                        int x = (int) cursorX - box / 2;
                        int y = (int) (frameHeight - cursorY) - box / 2;
                        JNI.callV(SCISSOR_TEST, enable);
                        JNI.callV(x, y, box, box, scissor);
                        JNI.callV(1f, 0.84f, 0.28f, 1f, clearColor);
                        JNI.callV(COLOR_BUFFER_BIT, clear);
                        JNI.callV(SCISSOR_TEST, disable);
                    }
                    int error = JNI.callI(getError);
                    if (error != 0) throw new IllegalStateException("GLES error 0x" + Integer.toHexString(error));
                    GLFW.glfwSwapBuffers(window);
                    if (touches != lastTouches) {
                        System.out.println("WMCL_GLFW_FRAME " + touches);
                        lastTouches = touches;
                    }
                    tick++;
                    if ((tick & 15) == 0) Thread.sleep(4);
                }
                System.out.println("WMCL_GLFW_OK " + version + " " + frameWidth + "x" + frameHeight + " frames=" + tick);
            }
        } finally {
            GLFW.glfwDestroyWindow(window);
            GLFW.glfwTerminate();
        }
    }

    private static final int[] sizeWidth = new int[1];
    private static final int[] sizeHeight = new int[1];
    private static double cursorX;
    private static double cursorY;
    private static int frameWidth = 1280;
    private static int frameHeight = 720;

    private static long require(String name) {
        long address = GLFW.glfwGetProcAddress(name);
        if (address == 0) throw new IllegalStateException("Missing GLES entry point: " + name);
        return address;
    }
}
