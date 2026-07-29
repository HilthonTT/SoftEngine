// Test classes otherwise run concurrently with one another, and some of what the suite
// tests is reached through a static seam rather than an argument —
// ScanlineRasterizer.VectorizedSpans, which a test flips to render one scene both ways and
// compare the results. A static that one test writes while another reads is a race, and a
// race in a test suite whose entire subject is deterministic rendering is worth more to
// remove than the wall-clock it costs: the whole suite runs in a couple of seconds.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
