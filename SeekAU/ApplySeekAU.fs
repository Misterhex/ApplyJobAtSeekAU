module ApplySeekAU

open System;
open System.Collections.Generic;
open System.Configuration;
open System.Linq;
open System.Text
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open OpenQA.Selenium
open OpenQA.Selenium.Chrome
open OpenQA.Selenium.Firefox
open OpenQA.Selenium.PhantomJS
open OpenQA.Selenium.Support.UI
open System.Net
open System.Collections.Generic
open Newtonsoft.Json
open System.IO

let private path = Path.Combine(Environment.CurrentDirectory,"appliedJobIds.txt")

let private getHashSet = 
        try 
            let json = File.ReadAllText(path)
            Some(JsonConvert.DeserializeObject<HashSet<string>>(json))
        with
        | :? FileNotFoundException -> Some(new HashSet<string>())
        | _ -> printfn "unable to get %A from disk" path; Environment.Exit(1); None

let private saveHashSet (set:HashSet<string>) =
        try
            let json = JsonConvert.SerializeObject(set)
            File.WriteAllText(path,json)
        with 
            _ -> printfn "unable to save %A from disk" path; Environment.Exit(1)

let private saveJobId jobId =
    try
        let hashSet = getHashSet

        hashSet.Value.Add(jobId) |> ignore
        saveHashSet hashSet.Value
        printfn "saved jobId %A" |> ignore
    with
        | _ -> printfn "unable to save job id %A" jobId; Environment.Exit(1)

let private getPageSourceAsync searchUrl (driver:IWebDriver) = 
    async {
        try
            printfn "querying seek au for jobids ..."
            driver.Url <- searchUrl
    
            let tryCloseToolTip =
                try
                    let driverWait = new WebDriverWait(driver, TimeSpan.FromSeconds(5.0))
                    let closeToolTip = driverWait.Until(fun driver -> driver.FindElement(By.Id("btnSearchTooltipClose")))
                    closeToolTip.Click() |> ignore
                    ignore
                with
                    | _-> ignore

            tryCloseToolTip()         
            
            new WebDriverWait(driver, TimeSpan.FromSeconds(15.0)) |> (fun x -> x.Until(fun x-> x.FindElement(By.Id("SortMode")))) |> ignore
            do! Async.Sleep(5000)   
            return Some(driver.PageSource)
        with
            | _-> return None
    }



let private getJobIds numberOfPastPageToSearch driver keywords=    
    let searchUrlFormat = @"http://www.seek.com.au/jobs/in-australia/#dateRange=31&workType=0&industry=&occupation=&graduateSearch=false&salaryFrom=0&salaryTo=999999&salaryType=annual&advertiserID=&advertiserGroup=&keywords={0}&page={1}&isAreaUnspecified=false&location=&area=&nation=3000&sortMode=ListedDate&searchFrom=quick&searchType="
    let regex = new Regex(@"data-jobid=""[0-9]{8}""")
    keywords 
        |> Array.map (fun keyword -> WebUtility.UrlEncode(keyword))
        |> Array.collect (fun keyword -> [|1.. numberOfPastPageToSearch|]|> Array.map(fun index -> String.Format(searchUrlFormat,keyword,index)))
        |> Array.map (fun searchUrl -> getPageSourceAsync searchUrl driver |> Async.RunSynchronously)
        |> Array.filter (fun x -> x.IsSome)
        |> Array.map (fun x-> x.Value)
        |> Array.toSeq
        |> Seq.collect (fun pageSource -> seq { for i in regex.Matches(pageSource) do yield i.ToString().Replace(@"data-jobid=""", "").Replace(@"""", "") })

let private applySingleJob (driver:IWebDriver) (jobId:string) email phoneNumber firstName lastName jobTitle companyname yearsOfExperience = 
    try
        driver.Url <- String.Format("https://www.seek.com.au/Apply/{0}", jobId)
        let wait30Seconds = new WebDriverWait(driver, TimeSpan.FromSeconds(10.0))
        wait30Seconds.Until(fun driver -> driver.FindElement(By.Id("PhoneNumber"))) |> (fun x-> x.Clear(); x.SendKeys(phoneNumber))
        wait30Seconds.Until(fun driver -> driver.FindElement(By.Id("Email"))) |> (fun x-> x.Clear(); x.SendKeys(email))
        wait30Seconds.Until(fun driver -> driver.FindElement(By.Id("LastName"))) |> (fun x-> x.Clear();x.SendKeys(lastName))
        wait30Seconds.Until(fun driver -> driver.FindElement(By.Id("FirstName"))) |> (fun x-> x.Clear();x.SendKeys(firstName))
        wait30Seconds.Until(fun driver -> driver.FindElement(By.Id("companyName"))) |> (fun x-> x.Clear();x.SendKeys(companyname))
        wait30Seconds.Until(fun driver -> driver.FindElement(By.Id("jobTitle"))) |> (fun x-> x.Clear(); x.SendKeys(jobTitle))
        wait30Seconds.Until(fun driver -> driver.FindElement(By.Id("ResumeTypeStored"))) |> (fun x-> x.Click())
        wait30Seconds.Until(fun driver -> driver.FindElement(By.CssSelector(String.Format(@"#timeInRoleYears > option:nth-child({0})", yearsOfExperience + 2)))) |> (fun x-> x.Click())
        wait30Seconds.Until(fun driver -> driver.FindElement(By.Id("SubmitLink"))) |> (fun x-> x.Click())

        match driver.PageSource with
        | source when source.ToLower().Contains("best of luck") -> saveJobId jobId; true
        | _-> false                  
    with
        _-> false

let private loginAsync username password (driver:IWebDriver)= 
    async {
        driver.Url <- "http://www.seek.com.au/";
        let wait10Seconds = new WebDriverWait(driver, TimeSpan.FromSeconds(10.0))
        wait10Seconds.Until(fun driver -> driver.FindElement(By.Id("username"))) |> (fun x -> x.SendKeys(username))
        wait10Seconds.Until(fun driver -> driver.FindElement(By.Id("password"))) |> (fun x -> x.SendKeys(password))
        wait10Seconds.Until(fun driver -> driver.FindElement(By.Id("logInButton"))) |> (fun x -> x.Click())

        do! Async.Sleep(250)
        }

let private applyJobs email password phoneNumber firstName lastName jobTitle companyname yearsOfExperience driver jobIds=
    let rec applyJobsRec jobIds=
        match jobIds with
        | [] -> printfn "job application completed"
        | currentJobId :: rest -> 
            let isSuccessful = applySingleJob driver currentJobId email phoneNumber firstName lastName jobTitle companyname yearsOfExperience
            match isSuccessful with
            | true -> printfn "application to jobId %A successful!" currentJobId
            | false -> printfn "application to jobId %A failed!" currentJobId
            applyJobsRec rest

    applyJobsRec(jobIds)
 
let startApply :unit = 
    let username = System.Configuration.ConfigurationManager.AppSettings.Get("username")
    let password = System.Configuration.ConfigurationManager.AppSettings.Get("password")
    let phone = System.Configuration.ConfigurationManager.AppSettings.Get("phone")
    let firstname = System.Configuration.ConfigurationManager.AppSettings.Get("firstname")
    let lastname = System.Configuration.ConfigurationManager.AppSettings.Get("lastname")
    let companyname = System.Configuration.ConfigurationManager.AppSettings.Get("companyname")
    let jobtitle = System.Configuration.ConfigurationManager.AppSettings.Get("jobtitle")
    let yearOfExperience = Int32.Parse(System.Configuration.ConfigurationManager.AppSettings.Get("yearofexperience"))
    let numberOfPastPageToSearchPerKeyword = Int32.Parse(System.Configuration.ConfigurationManager.AppSettings.Get("numberOfPastPageToSearchPerKeyword"))
    let keywords = System.Configuration.ConfigurationManager.AppSettings.Get("keywords").Split [|';'|]

    printfn "username %A" username
    printfn "password %A" password
    printfn "phone %A"phone
    printfn "firstname %A" firstname
    printfn "lastname %A" lastname
    printfn "companyname %A" companyname
    printfn "jobtitle %+A" jobtitle
    printfn "yearofexperience %A" yearOfExperience
    printfn "numberOfPastPageToSearchPerKeyword %A" numberOfPastPageToSearchPerKeyword
    printfn "keywords %A" keywords
    printfn "webkit %A" (System.Configuration.ConfigurationManager.AppSettings.Get("webkit"))

    use driver : IWebDriver = 
        let webkit = System.Configuration.ConfigurationManager.AppSettings.Get("webkit")
        match webkit with
        | "firefox" -> new FirefoxDriver() :> IWebDriver
        | _ -> new PhantomJSDriver() :> IWebDriver

    printfn "finding job from keywords %A" keywords
    printfn "search up to past %A page/s per keyword" numberOfPastPageToSearchPerKeyword
    let jobIds = (getJobIds numberOfPastPageToSearchPerKeyword driver keywords) |> Seq.distinct |> Seq.toList
    printfn "found a total of %i job ids from search" (jobIds.Length)

    let optionHashSet = getHashSet 
    if optionHashSet.IsNone then printfn "unable to read %A from disk" path; Environment.Exit(1)

    let uniqueJobIds = jobIds.Except(optionHashSet.Value) |> Seq.toList

    printfn "%i job ids have not been applied yet, applying now" (uniqueJobIds.Count())
    uniqueJobIds |> Seq.iter (printf " %A")
    printfn ""
    loginAsync username password driver |> Async.RunSynchronously
    uniqueJobIds |> Seq.toList |> applyJobs username password phone firstname lastname jobtitle companyname yearOfExperience driver |> ignore
    printfn "completed applying all jobs"
    



    