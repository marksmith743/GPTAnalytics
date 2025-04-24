This repository is just an example of how razor pages can interact with Azure openai and be able to upload files and instructions for analysis and download result csv files.

Installation Instructions:
Create a data folder that can be used to store temporary files.  Update the RootDirectory in the appsettings.json with that value.

Create an enviornment variable named AzureOpenAIUrl to store the URL to Azure open AI.  The AzureOpenAIUrl should look something like this: https://xxxxx.cognitiveservices.azure.com/

Create an enviornment variable named AzureOpenAIKey to store the Azure open AI key.

Don't forget to restart visual studio after adding the environment variables.

