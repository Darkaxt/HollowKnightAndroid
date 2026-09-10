package dev.silksong.shell;

public final class SecondaryDisplayTest
{
    private static int assertions;

    private static void check(boolean value, String message)
    {
        assertions++;
        if (!value) throw new AssertionError(message);
    }

    public static void main(String[] args)
    {
        SecondaryDisplay.TouchBuffer buffer = new SecondaryDisplay.TouchBuffer();
        buffer.setSurface(1240, 1080, true);
        buffer.offer(7, 0, 0.25, 0.1, 4000000001L);
        buffer.offer(7, 3, 0.25, 0.1, 4000000010L);
        double[] frame = buffer.drain();
        check(frame.length == 14, "Complete down/up must survive between frames");
        check(frame[1] == 1240 && frame[2] == 1080 && frame[3] == 1, "Missing surface bounds");
        check(frame[4] == 7 && frame[5] == 0 && frame[9] == 7 && frame[10] == 3, "Lost pointer order");
        check(frame[6] == 0.25 && frame[7] == 0.1, "Changed surface-local coordinates");
        check(frame[8] == 4000000001L, "Long device uptime lost timestamp precision");
        check(buffer.drain().length == 4, "Events were replayed");

        double generation = frame[0];
        buffer.offer(1, 0, 0.5, 0.5, 10);
        buffer.setSurface(1240, 1080, false);
        buffer.offer(1, 3, 0.5, 0.5, 20);
        frame = buffer.drain();
        check(frame[0] > generation && frame[3] == 0 && frame.length == 4,
            "Backgrounding must cancel pending input");
        buffer.setSurface(1240, 1080, true);
        check(buffer.drain()[3] == 1, "Surface did not resume");

        buffer.offer(1, 0, 0.5, 0.5, 30);
        generation = buffer.drain()[0];
        buffer.setSurface(1240, 969, true);
        frame = buffer.drain();
        check(frame[0] > generation && frame[2] == 969 && frame.length == 4,
            "Resize must start a fresh gesture generation");

        for (int i = 0; i < SecondaryDisplay.TouchBuffer.CAPACITY; i++)
            check(buffer.offer(1, 1, 0.5, 0.5, i), "Queue overflowed early");
        check(!buffer.offer(1, 1, 0.5, 0.5, 999), "Overflow must be reported");
        frame = buffer.drain();
        check(frame.length == 4, "Overflow must cancel rather than replay a partial gesture");

        buffer.setSurface(0, 0, true);
        buffer.offer(1, 0, 0, 0, 0);
        frame = buffer.drain();
        check(frame[3] == 0 && frame.length == 4, "An unmeasured surface accepted a touch");
        System.out.println("Secondary display touch buffer: " + assertions + " assertions passed");
    }
}
