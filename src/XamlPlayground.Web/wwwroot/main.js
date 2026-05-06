import { dotnet } from './_framework/dotnet.js'

const isBrowser = typeof window != "undefined";
if (!isBrowser) {
    throw new Error(`Expected to be running in a browser`);
}

const dotnetRuntime = await dotnet
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();
const assemblyAssets = [
    ...(config.resources?.coreAssembly ?? []),
    ...(config.resources?.assembly ?? [])
]
    .filter(asset => typeof asset.name === "string" && asset.name.endsWith(".dll"))
    .map(asset => `${asset.virtualPath ?? asset.name}|${asset.name}`);

await dotnetRuntime.runMain(config.mainAssemblyName, [
    window.location.search,
    globalThis.document.baseURI,
    assemblyAssets.join(";")
]);
