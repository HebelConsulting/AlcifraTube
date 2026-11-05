package alcifra.tube.check;

import java.io.IOException;
import java.util.List;
import java.util.Map;
import java.util.concurrent.TimeUnit;
import okhttp3.OkHttpClient;
import okhttp3.RequestBody;
import okhttp3.ResponseBody;
import org.schabi.newpipe.extractor.NewPipe;
import org.schabi.newpipe.extractor.ServiceList;
import org.schabi.newpipe.extractor.channel.ChannelExtractor;
import org.schabi.newpipe.extractor.downloader.Downloader;
import org.schabi.newpipe.extractor.downloader.Request;
import org.schabi.newpipe.extractor.downloader.Response;
import org.schabi.newpipe.extractor.kiosk.KioskExtractor;
import org.schabi.newpipe.extractor.kiosk.KioskList;
import org.schabi.newpipe.extractor.services.peertube.PeertubeInstance;
import org.schabi.newpipe.extractor.services.peertube.PeertubeService;
import org.schabi.newpipe.extractor.stream.StreamExtractor;
import org.schabi.newpipe.extractor.stream.StreamInfoItem;
import org.schabi.newpipe.extractor.stream.VideoStream;

/**
 * What a person with NewPipe does with a video host (core #2142): adds it as a PeerTube instance, opens the catalogue,
 * opens a video, plays its file, opens its channel. Through the extractor's own code, never through a copy of its
 * requests, so a change in what the extractor reads shows up here. Exit code 1 names the first step that failed.
 */
public final class Check {
    private Check() {
    }

    public static void main(final String[] args) throws Exception {
        final String base = args.length > 0 ? args[0] : "https://tube.test";
        final OkHttpClient http = new OkHttpClient.Builder()
                .followRedirects(true).followSslRedirects(true)
                .readTimeout(30, TimeUnit.SECONDS).build();
        NewPipe.init(new OkDownloader(http));
        final PeertubeService service = ServiceList.PeerTube;

        final PeertubeInstance instance = new PeertubeInstance(base);
        instance.fetchInstanceMetaData();
        service.setInstance(instance);
        step("instance: " + instance.getName() + " at " + instance.getUrl());

        final KioskList kiosks = service.getKioskList();
        StreamInfoItem first = null;
        for (final String id : kiosks.getAvailableKiosks()) {
            final KioskExtractor<?> kiosk = kiosks.getExtractorById(id, null);
            kiosk.fetchPage();
            final List<?> items = kiosk.getInitialPage().getItems();
            step("kiosk " + id + ": " + items.size() + " item(s)");
            if (first == null && !items.isEmpty() && items.get(0) instanceof StreamInfoItem item) {
                first = item;
            }
        }
        if (first == null) {
            fail("no kiosk listed the seeded clip");
        }

        final StreamExtractor stream = service.getStreamExtractor(first.getUrl());
        stream.fetchPage();
        step("stream: '" + stream.getName() + "', " + stream.getLength() + " s, uploader " + stream.getUploaderName());
        final List<VideoStream> files = stream.getVideoStreams();
        if (files.isEmpty()) {
            fail("the stream lists no video file");
        }
        final String fileUrl = files.get(0).getContent();
        try (okhttp3.Response range = http.newCall(new okhttp3.Request.Builder()
                .url(fileUrl).header("Range", "bytes=0-99").build()).execute()) {
            if (range.code() != 206) {
                fail("a range request on the file answered " + range.code() + " (expected 206) at " + fileUrl);
            }
            step("file: 206 for the first 100 bytes of " + fileUrl);
        }

        final ChannelExtractor channel = service.getChannelExtractor(stream.getUploaderUrl());
        channel.fetchPage();
        step("channel: '" + channel.getName() + "'");
        System.out.println("NewPipe " + NewPipe.class.getPackage().getImplementationVersion() + ": every step passed");
    }

    private static void step(final String what) {
        System.out.println("ok   " + what);
    }

    private static void fail(final String why) {
        System.err.println("FAIL " + why);
        System.exit(1);
    }

    /** The extractor's requests over OkHttp, shaped like the extractor's own test downloader. */
    private static final class OkDownloader extends Downloader {
        private final OkHttpClient client;

        OkDownloader(final OkHttpClient client) {
            this.client = client;
        }

        @Override
        public Response execute(final Request request) throws IOException {
            final byte[] data = request.dataToSend();
            final okhttp3.Request.Builder builder = new okhttp3.Request.Builder()
                    .method(request.httpMethod(), data == null ? null : RequestBody.create(data))
                    .url(request.url())
                    .addHeader("User-Agent", "AlcifraTube client check");
            for (final Map.Entry<String, List<String>> header : request.headers().entrySet()) {
                builder.removeHeader(header.getKey());
                for (final String value : header.getValue()) {
                    builder.addHeader(header.getKey(), value);
                }
            }
            try (okhttp3.Response response = client.newCall(builder.build()).execute()) {
                String body = null;
                try (ResponseBody responseBody = response.body()) {
                    if (responseBody != null) {
                        body = responseBody.string();
                    }
                }
                return new Response(response.code(), response.message(), response.headers().toMultimap(), body,
                        response.request().url().toString());
            }
        }
    }
}
