[<AutoOpen>]
module Farmer.Arm.Web

open Farmer
open Farmer.CoreTypes
open Farmer.Web
open System

type ServerFarm =
    { Name : ResourceName
      Location : Location
      Sku: Sku
      WorkerSize : WorkerSize
      WorkerCount : int
      OperatingSystem : OS }
    member this.IsDynamic =
        match this.Sku, this.WorkerSize with
        | Isolated "Y1", Serverless -> true
        | _ -> false
    member this.Reserved =
        match this.OperatingSystem with
        | Linux -> true
        | Windows -> false
    member this.Kind =
        match this.OperatingSystem with
        | Linux -> Some "linux"
        | _ -> None
    member this.Tier =
        match this.Sku with
        | Free -> "Free"
        | Shared -> "Shared"
        | Basic _ -> "Basic"
        | Standard _ -> "Standard"
        | Premium _ -> "Premium"
        | PremiumV2 _ -> "PremiumV2"
        | Dynamic -> "Dynamic"
        | Isolated _ -> "Isolated"
    interface IArmResource with
        member this.ResourceName = this.Name
        member this.JsonModel =
            {| ``type`` = "Microsoft.Web/serverfarms"
               sku =
                   {| name =
                        match this.Sku with
                        | Free ->
                            "F1"
                        | Shared ->
                            "D1"
                        | Basic sku
                        | Standard sku
                        | Premium sku
                        | PremiumV2 sku
                        | Isolated sku ->
                            sku
                        | Dynamic ->
                            "Y1"
                      tier = this.Tier
                      size =
                        match this.WorkerSize with
                        | Small -> "0"
                        | Medium -> "1"
                        | Large -> "2"
                        | Serverless -> "Y1"
                      family = if this.IsDynamic then "Y" else null
                      capacity = if this.IsDynamic then 0 else this.WorkerCount |}
               name = this.Name.Value
               apiVersion = "2018-02-01"
               location = this.Location.ArmValue
               properties =
                    {| name = this.Name.Value
                       computeMode = if this.IsDynamic then "Dynamic" else null
                       perSiteScaling = if this.IsDynamic then Nullable() else Nullable false
                       reserved = this.Reserved |}
               kind = this.Kind |> Option.toObj
            |} :> _

module ZipDeploy =
    open System.IO
    open System.IO.Compression

    type ZipDeployKind =
        | DeployFolder of string
        | DeployZip of string
        member this.Value = match this with DeployFolder s | DeployZip s -> s
        /// Tries to create a ZipDeployKind from a string path.
        static member TryParse path =
            if (File.GetAttributes path).HasFlag FileAttributes.Directory then
                Some(DeployFolder path)
            else if Path.GetExtension path = ".zip" then
                Some(DeployZip path)
            else
                None
        /// Processes a ZipDeployKind and returns the filename of the zip file.
        /// If the ZipDeployKind is a DeployFolder, the folder will be zipped first and the generated zip file returned.
        member this.GetZipPath targetFolder =
            match this with
            | DeployFolder appFolder ->
                let packageFilename = Path.Combine(targetFolder, (Path.GetFileName appFolder) + ".zip")
                File.Delete packageFilename
                ZipFile.CreateFromDirectory(appFolder, packageFilename)
                packageFilename
            | DeployZip zipFilePath ->
                zipFilePath

type Sites =
    { Name : ResourceName
      Location : Location
      ServicePlan : ResourceName
      AppSettings : List<string * string>
      AlwaysOn : bool
      HTTPSOnly : bool
      HTTP20Enabled : bool option
      ClientAffinityEnabled : bool option
      WebSocketsEnabled : bool option
      Dependencies : ResourceName list
      Kind : string
      Identity : FeatureFlag option
      LinuxFxVersion : string option
      AppCommandLine : string option
      NetFrameworkVersion : string option
      JavaVersion : string option
      JavaContainer : string option
      JavaContainerVersion : string option
      PhpVersion : string option
      PythonVersion : string option
      Metadata : List<string * string>
      ZipDeployPath : string option
      Parameters : SecureParameter list }
    interface IParameters with
        member this.SecureParameters = this.Parameters
    interface IPostDeploy with
        member this.Run resourceGroupName =
            match this with
            | { ZipDeployPath = Some path; Name = name } ->
                let path =
                    ZipDeploy.ZipDeployKind.TryParse path
                    |> Option.defaultWith (fun () ->
                        failwithf "Path '%s' must either be a folder to be zipped, or an existing zip." path)
                printfn "Running ZIP deploy for %s" path.Value
                Some(Deploy.Az.zipDeploy name.Value path.GetZipPath resourceGroupName)
            | _ ->
                None
    interface IArmResource with
        member this.ResourceName = this.Name
        member this.JsonModel =
            {| ``type`` = "Microsoft.Web/sites"
               name = this.Name.Value
               apiVersion = "2016-08-01"
               location = this.Location.ArmValue
               dependsOn = this.Dependencies |> List.map(fun p -> p.Value)
               kind = this.Kind
               identity =
                 match this.Identity with
                 | Some Enabled -> box {| ``type`` = "SystemAssigned" |}
                 | Some Disabled -> box {| ``type`` = "None" |}
                 | None -> null
               properties =
                   {| serverFarmId = this.ServicePlan.Value
                      httpsOnly = this.HTTPSOnly
                      clientAffinityEnabled = match this.ClientAffinityEnabled with Some v -> box v | None -> null
                      siteConfig =
                           [ "alwaysOn", box this.AlwaysOn
                             "appSettings", this.AppSettings |> List.map(fun (k,v) -> {| name = k; value = v |}) |> box
                             match this.LinuxFxVersion with Some v -> "linuxFxVersion", box v | None -> ()
                             match this.AppCommandLine with Some v -> "appCommandLine", box v | None -> ()
                             match this.NetFrameworkVersion with Some v -> "netFrameworkVersion", box v | None -> ()
                             match this.JavaVersion with Some v -> "javaVersion", box v | None -> ()
                             match this.JavaContainer with Some v -> "javaContainer", box v | None -> ()
                             match this.JavaContainerVersion with Some v -> "javaContainerVersion", box v | None -> ()
                             match this.PhpVersion with Some v -> "phpVersion", box v | None -> ()
                             match this.PythonVersion with Some v -> "pythonVersion", box v | None -> ()
                             match this.HTTP20Enabled with Some v -> "http20Enabled", box v | None -> ()
                             match this.WebSocketsEnabled with Some v -> "webSocketsEnabled", box v | None -> ()
                             "metadata", this.Metadata |> List.map(fun (k,v) -> {| name = k; value = v |}) |> box ]
                           |> Map.ofList
                    |}
            |} :> _
