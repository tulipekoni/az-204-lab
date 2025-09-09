const http = require("http");
const port = process.env.PORT || 8080;
const secret = process.env.MY_SECRET || "(no secret)";
const appinsights = require("applicationinsights");
if (process.env.APPLICATIONINSIGHTS_CONNECTION_STRING) {
  appinsights.setup().start();
  appinsights.defaultClient.trackEvent({ name: "AppStarted" });
}
const server = http.createServer((req, res) => {
  if (req.url === "/fail") {
    throw new Error("Boom");
  }
  res.end(`Hello Secure World! Secret=${secret}\n`);
});
server.listen(port);
