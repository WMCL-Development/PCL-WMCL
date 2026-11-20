public final class WmclJvmProbe {
    public static void main(String[] args) {
        if (args.length != 3 || !args[0].equals("space and \"quotes\" \\ tail") || !args[1].isEmpty()
                || !args[2].equals("\u4e2d\u6587\ud83c\udf43")
                || !"two words".equals(System.getProperty("wmcl.probe"))) {
            throw new IllegalArgumentException("WMCL launch arguments were not preserved");
        }
        System.out.println("WMCL_JVM_OK " + System.getProperty("java.version"));
    }
}
