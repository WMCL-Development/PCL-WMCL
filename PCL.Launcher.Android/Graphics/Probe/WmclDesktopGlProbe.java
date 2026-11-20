import org.lwjgl.glfw.GLFW;
import org.lwjgl.opengl.GL;
import org.lwjgl.opengl.GL11;
import org.lwjgl.opengl.GL15;
import org.lwjgl.opengl.GL20;
import org.lwjgl.opengl.GL30;
import org.lwjgl.system.MemoryStack;
import java.nio.FloatBuffer;

/**
 * Drives Minecraft's rendering API (desktop GL through the LWJGL bindings) on Android,
 * reporting which GLSL dialects the configured translator accepts and then drawing a
 * triangle with the best one it does accept.
 */
public final class WmclDesktopGlProbe {
    private static final String[] VERSIONS = {"150", "130", "120", "110", "300 es", "100"};

    /** Line-buffered output so a killed JVM still leaves evidence. */
    private static void say(String line) {
        System.out.println(line);
        System.out.flush();
    }

    public static void main(String[] args) {
        try {
            run();
        } catch (Throwable failure) {
            // Uncaught stack traces can be lost with the process; report through the same
            // flushed stdout the rest of the probe uses.
            say("WMCL_GL_FAILED " + failure);
            for (StackTraceElement frame : failure.getStackTrace())
                say("  at " + frame);
            if (failure instanceof RuntimeException) throw (RuntimeException) failure;
            if (failure instanceof Error) throw (Error) failure;
            throw new RuntimeException(failure);
        }
    }

    private static void run() throws InterruptedException {
        say("WMCL_GL_BUILD probe-12");
        say("WMCL_GL_STAGE start");
        if (!GLFW.glfwInit()) throw new IllegalStateException("glfwInit failed");
        say("WMCL_GL_STAGE glfwInit");
        long window = GLFW.glfwCreateWindow(1280, 720, "WMCL GL", 0, 0);
        if (window == 0) throw new IllegalStateException("glfwCreateWindow failed");
        try {
            GLFW.glfwMakeContextCurrent(window);
            say("WMCL_GL_STAGE context-current");
            GL.createCapabilities();
            say("WMCL_GL_STAGE capabilities");
            say("WMCL_GL_CONTEXT " + GL11.glGetString(GL11.GL_VERSION)
                + " | " + GL11.glGetString(GL11.GL_RENDERER));
            say("WMCL_GL_SHADING " + GL11.glGetString(GL20.GL_SHADING_LANGUAGE_VERSION));

            String version = null;
            for (String candidate : VERSIONS) {
                String log = compile(candidate, "-1.0", "-1.0");
                boolean ok = log == null;
                say("WMCL_GL_SHADER " + candidate + " ok=" + ok
                    + (ok ? "" : " log=" + log.substring(0, Math.min(160, log.length()))));
                if (ok && version == null) version = candidate;
            }
            if (version == null) throw new IllegalStateException("no usable GLSL dialect");
            say("WMCL_GL_DIALECT " + version);

            int program = buildProgram(version);
            int vao = GL30.glGenVertexArrays();
            GL30.glBindVertexArray(vao);
            int vbo = GL15.glGenBuffers();
            GL15.glBindBuffer(GL15.GL_ARRAY_BUFFER, vbo);
            try (MemoryStack stack = MemoryStack.stackPush()) {
                FloatBuffer data = stack.floats(
                    -0.7f, -0.7f, 1f, 0f, 0f,
                     0.7f, -0.7f, 0f, 1f, 0f,
                     0f, 0.7f, 0f, 0f, 1f);
                GL15.glBufferData(GL15.GL_ARRAY_BUFFER, data, GL15.GL_STATIC_DRAW);
            }
            int position = GL20.glGetAttribLocation(program, "position");
            int color = GL20.glGetAttribLocation(program, "color");
            GL20.glEnableVertexAttribArray(position);
            GL20.glVertexAttribPointer(position, 2, GL11.GL_FLOAT, false, 20, 0);
            GL20.glEnableVertexAttribArray(color);
            GL20.glVertexAttribPointer(color, 3, GL11.GL_FLOAT, false, 20, 8);

            int frames = 0;
            boolean lit = false;
            while (!GLFW.glfwWindowShouldClose(window)) {
                GLFW.glfwPollEvents();
                int[] width = new int[1], height = new int[1];
                GLFW.glfwGetFramebufferSize(window, width, height);
                GL11.glViewport(0, 0, Math.max(1, width[0]), Math.max(1, height[0]));
                GL11.glClearColor(0.05f, 0.07f, 0.12f, 1f);
                GL11.glClear(GL11.GL_COLOR_BUFFER_BIT);
                GL20.glUseProgram(program);
                GL30.glBindVertexArray(vao);
                GL11.glDrawArrays(GL11.GL_TRIANGLES, 0, 3);
                int error = GL11.glGetError();
                if (error != 0) throw new IllegalStateException("GL error 0x" + Integer.toHexString(error));
                if (frames == 0) lit = hasTriangle(width[0], height[0]);
                GLFW.glfwSwapBuffers(window);
                frames++;
                if ((frames & 15) == 0) Thread.sleep(4);
            }
            say("WMCL_GL_OK frames=" + frames + " triangleReadback=" + lit);
        } finally {
            GLFW.glfwDestroyWindow(window);
            GLFW.glfwTerminate();
        }
    }

    private static String header(String version) {
        return "#version " + version + "\n";
    }

    private static boolean legacy(String version) {
        return version.equals("120") || version.equals("110");
    }

    /** Compiles both stages; returns null on success or the first error log. */
    private static String compile(String version, String unusedA, String unusedB) {
        int vs = GL20.glCreateShader(GL20.GL_VERTEX_SHADER);
        GL20.glShaderSource(vs, vertexSource(version));
        GL20.glCompileShader(vs);
        if (GL20.glGetShaderi(vs, GL20.GL_COMPILE_STATUS) == GL11.GL_FALSE) {
            String log = GL20.glGetShaderInfoLog(vs).replace('\n', ' ');
            GL20.glDeleteShader(vs);
            return "vertex: " + log;
        }
        GL20.glDeleteShader(vs);
        return null;
    }

    private static String vertexSource(String version) {
        StringBuilder text = new StringBuilder(header(version));
        if (version.equals("300 es")) text.append("precision highp float;\n");
        text.append(legacy(version) ? "attribute vec2 position;\nattribute vec3 color;\nvarying vec3 passColor;\n"
                                    : "in vec2 position;\nin vec3 color;\nout vec3 passColor;\n");
        text.append("void main() {\n    passColor = color;\n    gl_Position = vec4(position, 0.0, 1.0);\n}\n");
        return text.toString();
    }

    private static String fragmentSource(String version) {
        StringBuilder text = new StringBuilder(header(version));
        if (version.equals("300 es")) text.append("precision highp float;\n");
        text.append(legacy(version) ? "varying vec3 passColor;\n" : "in vec3 passColor;\n");
        text.append("out vec4 fragColor;\n");
        text.append("void main() { fragColor = vec4(passColor, 1.0); }\n");
        return text.toString();
    }

    private static int buildProgram(String version) {
        int program = GL20.glCreateProgram();
        int vs = GL20.glCreateShader(GL20.GL_VERTEX_SHADER);
        GL20.glShaderSource(vs, vertexSource(version));
        GL20.glCompileShader(vs);
        checkShader(vs, "vertex");
        int fs = GL20.glCreateShader(GL20.GL_FRAGMENT_SHADER);
        GL20.glShaderSource(fs, fragmentSource(version));
        GL20.glCompileShader(fs);
        checkShader(fs, "fragment");
        GL20.glAttachShader(program, vs);
        GL20.glAttachShader(program, fs);
        GL20.glBindAttribLocation(program, 0, "position");
        GL20.glBindAttribLocation(program, 1, "color");
        GL20.glLinkProgram(program);
        if (GL20.glGetProgrami(program, GL20.GL_LINK_STATUS) == GL11.GL_FALSE)
            throw new IllegalStateException("link: " + GL20.glGetProgramInfoLog(program));
        return program;
    }

    private static boolean hasTriangle(int width, int height) {
        try (MemoryStack stack = MemoryStack.stackPush()) {
            java.nio.ByteBuffer pixel = stack.malloc(4);
            GL11.glReadPixels(Math.max(1, width) / 2, Math.max(1, height) / 2, 1, 1,
                GL11.GL_RGBA, GL11.GL_UNSIGNED_BYTE, pixel);
            return (pixel.get(0) & 255) > 40 || (pixel.get(1) & 255) > 40 || (pixel.get(2) & 255) > 40;
        }
    }

    private static void checkShader(int shader, String label) {
        if (GL20.glGetShaderi(shader, GL20.GL_COMPILE_STATUS) == GL11.GL_FALSE)
            throw new IllegalStateException(label + " shader: " + GL20.glGetShaderInfoLog(shader));
    }
}
